using Microsoft.EntityFrameworkCore;
using Portlite.Api.Mappings;
using Portlite.Domain.Common;
using Portlite.Domain.Entities;
using Portlite.Domain.Enums;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class PortfolioSnapshotService
{
    private readonly PortliteDbContext _db;
    private readonly PositionCalculator _positions;
    private readonly IPriceProvider _prices;
    private readonly PriceSnapshotStore _priceStore;
    private readonly ILogger<PortfolioSnapshotService> _log;

    public PortfolioSnapshotService(
        PortliteDbContext db,
        PositionCalculator positions,
        IPriceProvider prices,
        PriceSnapshotStore priceStore,
        ILogger<PortfolioSnapshotService> log)
    {
        _db = db;
        _positions = positions;
        _prices = prices;
        _priceStore = priceStore;
        _log = log;
    }

    private const string BenchmarkSymbol = "SPY";

    public async Task<PortfolioSnapshotDto> CreateOrUpdateSnapshotAsync(
        Guid subPortfolioId,
        CancellationToken ct = default)
    {
        var portfolio = await _db.SubPortfolios.FindAsync([subPortfolioId], ct)
            ?? throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        await EnsureBenchmarkAssetAsync(ct);
        var positions = await _positions.CalculateForPortfolioAsync(subPortfolioId, ct);
        var openPositions = positions.Where(p => p.Quantity > 0).ToList();

        // Step 1: HTTP fetch all quotes in parallel (SPY + each open position).
        // 11 sequential calls (~1.7s) become 11 parallel calls (~150ms).
        var distinctSymbols = openPositions.Select(p => p.AssetSymbol)
            .Append(BenchmarkSymbol).Distinct().ToList();
        var fetchTasks = distinctSymbols.Select(sym => FetchQuoteSafelyAsync(sym, ct)).ToArray();
        var fetchedQuotes = await Task.WhenAll(fetchTasks);
        var quoteMap = distinctSymbols.Zip(fetchedQuotes).ToDictionary(t => t.First, t => t.Second);

        // Step 2: Persist quotes — batched DB ops (single load + single SaveChanges).
        // Avoids N×2 round trips against Azure SQL.
        // Load existing price snapshots for all relevant dates (today + quote timestamps).
        var quoteDates = fetchedQuotes.Where(q => q is not null)
            .Select(q => DateOnly.FromDateTime(q!.Timestamp))
            .Append(DateOnly.FromDateTime(DateTime.UtcNow))
            .Distinct().ToList();
        var existingPrices = await _db.PriceSnapshots
            .Where(p => quoteDates.Contains(p.Date) && distinctSymbols.Contains(p.AssetSymbol))
            .ToDictionaryAsync(p => (p.AssetSymbol, p.Date), ct);
        foreach (var quote in fetchedQuotes)
        {
            if (quote is null) continue;
            var quoteDate = DateOnly.FromDateTime(quote.Timestamp);
            if (existingPrices.TryGetValue((quote.Symbol, quoteDate), out var price))
            {
                price.Close = quote.Current;
                price.PreviousClose = quote.PreviousClose;
                price.Open = quote.DayOpen ?? price.Open;
                price.High = quote.DayHigh ?? price.High;
                price.Low = quote.DayLow ?? price.Low;
                price.Source = quote.Source;
            }
            else
            {
                var newSnap = new PriceSnapshot
                {
                    AssetSymbol = quote.Symbol,
                    Date = quoteDate,
                    Close = quote.Current,
                    PreviousClose = quote.PreviousClose,
                    Open = quote.DayOpen,
                    High = quote.DayHigh,
                    Low = quote.DayLow,
                    Source = quote.Source
                };
                _db.PriceSnapshots.Add(newSnap);
                existingPrices[(quote.Symbol, quoteDate)] = newSnap;
            }
        }
        await _db.SaveChangesAsync(ct);

        // Step 3: Resolve final price for each position, falling back to last stored on failure.
        var reportingCurrency = CurrencyCode.USD;
        decimal marketValueAmount = 0m;
        decimal costBasisAmount = 0m;
        decimal realizedPnLAmount = 0m;
        var missing = new List<string>();

        foreach (var pos in openPositions)
        {
            decimal? currentPrice = quoteMap.GetValueOrDefault(pos.AssetSymbol)?.Current;
            if (currentPrice is null)
            {
                var last = await _priceStore.GetLatestAsync(pos.AssetSymbol, ct);
                if (last is null) { missing.Add(pos.AssetSymbol); continue; }
                currentPrice = last.Close;
            }

            var multiplier = pos.AssetType == AssetType.Option ? 100m : 1m;
            marketValueAmount += pos.Quantity * currentPrice.Value * multiplier;
            costBasisAmount += pos.TotalCost;
        }

        // Realized PnL across all (closed) positions, even those with quantity=0
        realizedPnLAmount = positions.Sum(p => p.RealizedPnL);

        var unrealizedPnLAmount = marketValueAmount - costBasisAmount;

        var cashBalance = await CalculateCashBalanceAsync(subPortfolioId, reportingCurrency, ct);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var existing = await _db.PortfolioValueSnapshots
            .FirstOrDefaultAsync(s => s.SubPortfolioId == subPortfolioId && s.Date == date, ct);

        var marketValue = new Money(marketValueAmount, reportingCurrency);
        var costBasis = new Money(costBasisAmount, reportingCurrency);
        var realizedPnL = new Money(realizedPnLAmount, reportingCurrency);
        var unrealizedPnL = new Money(unrealizedPnLAmount, reportingCurrency);

        if (existing is null)
        {
            existing = new PortfolioValueSnapshot
            {
                SubPortfolioId = subPortfolioId,
                Date = date,
                MarketValue = marketValue,
                CostBasis = costBasis,
                RealizedPnL = realizedPnL,
                UnrealizedPnL = unrealizedPnL
            };
            _db.PortfolioValueSnapshots.Add(existing);
        }
        else
        {
            existing.MarketValue = marketValue;
            existing.CostBasis = costBasis;
            existing.RealizedPnL = realizedPnL;
            existing.UnrealizedPnL = unrealizedPnL;
        }

        await _db.SaveChangesAsync(ct);

        var totalEquity = new Money(marketValueAmount + cashBalance.Amount, reportingCurrency);

        return new PortfolioSnapshotDto(
            subPortfolioId,
            date,
            marketValue.ToDto(),
            costBasis.ToDto(),
            realizedPnL.ToDto(),
            unrealizedPnL.ToDto(),
            cashBalance.ToDto(),
            totalEquity.ToDto(),
            positions.Count(p => p.Quantity > 0),
            missing);
    }

    public async Task<List<PortfolioSnapshotDto>> GetHistoryAsync(
        Guid subPortfolioId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        var portfolioExists = await _db.SubPortfolios.AnyAsync(x => x.Id == subPortfolioId, ct);
        if (!portfolioExists) throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        var query = _db.PortfolioValueSnapshots.Where(s => s.SubPortfolioId == subPortfolioId);
        if (from.HasValue) query = query.Where(s => s.Date >= from.Value);
        if (to.HasValue) query = query.Where(s => s.Date <= to.Value);

        var rows = await query.OrderBy(s => s.Date).ToListAsync(ct);

        var reportingCurrency = CurrencyCode.USD;

        // Her snapshot için nakit, O TARİHE kadarki işlemlerden hesaplanır (tarihe duyarlı).
        var result = new List<PortfolioSnapshotDto>();
        foreach (var r in rows)
        {
            var cash = await CalculateCashBalanceAsync(subPortfolioId, reportingCurrency, ct, asOf: r.Date);
            result.Add(new PortfolioSnapshotDto(
                r.SubPortfolioId,
                r.Date,
                r.MarketValue.ToDto(),
                r.CostBasis.ToDto(),
                r.RealizedPnL.ToDto(),
                r.UnrealizedPnL.ToDto(),
                cash.ToDto(),
                new Money(r.MarketValue.Amount + cash.Amount, reportingCurrency).ToDto(),
                PositionCount: 0,
                MissingPriceSymbols: new List<string>()
            ));
        }
        return result;
    }

    private async Task<QuoteResult?> FetchQuoteSafelyAsync(string symbol, CancellationToken ct)
    {
        try
        {
            return await _prices.GetQuoteAsync(symbol, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Quote fetch failed for {Symbol}", symbol);
            return null;
        }
    }

    private async Task EnsureBenchmarkAssetAsync(CancellationToken ct)
    {
        var exists = await _db.Assets.AnyAsync(a => a.Symbol == BenchmarkSymbol, ct);
        if (exists) return;
        _db.Assets.Add(new Asset
        {
            Symbol = BenchmarkSymbol,
            Name = "SPDR S&P 500 ETF Trust",
            Type = AssetType.Stock,
            Currency = CurrencyCode.USD
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Money> CalculateCashBalanceAsync(
        Guid subPortfolioId,
        CurrencyCode reportingCurrency,
        CancellationToken ct = default,
        DateOnly? asOf = null)
    {
        // asOf verilirse yalnızca o tarihe (gün sonu) kadarki hareketler/işlemler sayılır.
        DateTime? cutoff = asOf?.ToDateTime(TimeOnly.MaxValue);

        var txQuery = _db.CashTransactions.Where(c => c.SubPortfolioId == subPortfolioId);
        if (cutoff.HasValue) txQuery = txQuery.Where(c => c.OccurredAt <= cutoff.Value);
        var txs = await txQuery.ToListAsync(ct);

        decimal balance = 0m;
        foreach (var t in txs)
        {
            if (t.Amount.Currency != reportingCurrency)
                continue; // FX conversion not implemented yet — skip non-reporting currency
            balance += t.Type switch
            {
                CashTxType.Deposit or CashTxType.Dividend or CashTxType.Interest => t.Amount.Amount,
                CashTxType.Withdraw or CashTxType.Fee => -t.Amount.Amount,
                _ => 0m
            };
        }

        // Subtract cost of buys, add proceeds of sells (tarihe duyarlı).
        var tradeQuery = _db.Trades.Where(t => t.SubPortfolioId == subPortfolioId);
        if (cutoff.HasValue) tradeQuery = tradeQuery.Where(t => t.ExecutedAt <= cutoff.Value);
        var trades = await tradeQuery.ToListAsync(ct);

        foreach (var t in trades)
        {
            var notional = t.Quantity * t.Price;
            balance += t.Side == TradeSide.Buy ? -(notional + t.Fee) : (notional - t.Fee);
        }

        return new Money(balance, reportingCurrency);
    }
}
