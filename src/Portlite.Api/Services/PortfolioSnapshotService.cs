using Microsoft.EntityFrameworkCore;
using Portlite.Api.Mappings;
using Portlite.Domain.Common;
using Portlite.Domain.Entities;
using Portlite.Domain.Enums;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public record SnapshotBackfillResult(
    DateOnly From,
    DateOnly To,
    int Created,
    int Updated,
    int SymbolCount,
    List<string> MissingSymbols);

public class PortfolioSnapshotService
{
    private readonly PortliteDbContext _db;
    private readonly PositionCalculator _positions;
    private readonly IPriceProvider _prices;
    private readonly IHistoricalPriceProvider _history;
    private readonly PriceSnapshotStore _priceStore;
    private readonly ILogger<PortfolioSnapshotService> _log;

    public PortfolioSnapshotService(
        PortliteDbContext db,
        PositionCalculator positions,
        IPriceProvider prices,
        IHistoricalPriceProvider history,
        PriceSnapshotStore priceStore,
        ILogger<PortfolioSnapshotService> log)
    {
        _db = db;
        _positions = positions;
        _prices = prices;
        _history = history;
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

    // Geçmiş fiyatlarla ilk işlem tarihinden bugüne günlük snapshot üretir (tek seferlik backfill).
    public async Task<SnapshotBackfillResult> BackfillAsync(
        Guid subPortfolioId,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        var portfolioExists = await _db.SubPortfolios.AnyAsync(x => x.Id == subPortfolioId, ct);
        if (!portfolioExists) throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        var trades = await _db.Trades
            .Where(t => t.SubPortfolioId == subPortfolioId)
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync(ct);
        if (trades.Count == 0) throw new ValidationException("Backfill için işlem yok.");

        var start = from ?? DateOnly.FromDateTime(trades[0].ExecutedAt);
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var reportingCurrency = CurrencyCode.USD;

        // Sembol başına tek istekle tüm günlük kapanışları çek.
        var symbols = trades.Select(t => t.AssetSymbol).Distinct().ToList();
        var daysBack = (DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - start.DayNumber) + 10;
        var closes = new Dictionary<string, SortedList<DateOnly, decimal>>();
        var missingSymbols = new List<string>();
        foreach (var sym in symbols)
        {
            try
            {
                var bars = await _history.GetDailyBarsAsync(sym, daysBack, ct);
                if (bars.Count == 0) { missingSymbols.Add(sym); continue; }
                var list = new SortedList<DateOnly, decimal>();
                foreach (var b in bars) list[b.Date] = b.Close;
                closes[sym] = list;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Backfill: {Symbol} için geçmiş fiyat alınamadı", sym);
                missingSymbols.Add(sym);
            }
            await Task.Delay(250, ct); // rate limit nezaketi
        }

        var existing = await _db.PortfolioValueSnapshots
            .Where(s => s.SubPortfolioId == subPortfolioId && s.Date >= start && s.Date <= end)
            .ToDictionaryAsync(s => s.Date, ct);

        // İşlemleri kronolojik replay ederek her günün pozisyon durumunu üret.
        var state = new Dictionary<string, (decimal Qty, decimal TotalCost, decimal Realized)>();
        int tradeIdx = 0, created = 0, updated = 0;

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var dayEnd = date.ToDateTime(TimeOnly.MaxValue);
            while (tradeIdx < trades.Count && trades[tradeIdx].ExecutedAt <= dayEnd)
            {
                var t = trades[tradeIdx++];
                var s = state.GetValueOrDefault(t.AssetSymbol);
                if (t.Side == TradeSide.Buy)
                {
                    s.TotalCost += t.Quantity * t.Price + t.Fee;
                    s.Qty += t.Quantity;
                }
                else
                {
                    var avg = s.Qty > 0 ? s.TotalCost / s.Qty : 0m;
                    var soldQty = Math.Min(t.Quantity, s.Qty);
                    s.Realized += soldQty * (t.Price - avg) - t.Fee;
                    s.TotalCost -= avg * soldQty;
                    s.Qty -= t.Quantity;
                }
                state[t.AssetSymbol] = s;
            }

            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            decimal marketValue = 0m, costBasis = 0m;
            foreach (var (sym, s) in state)
            {
                if (s.Qty <= 0) continue;
                costBasis += s.TotalCost;
                var close = FindCloseOnOrBefore(closes.GetValueOrDefault(sym), date);
                if (close.HasValue) marketValue += s.Qty * close.Value;
            }
            var realizedTotal = state.Values.Sum(s => s.Realized);

            if (existing.TryGetValue(date, out var snap))
            {
                snap.MarketValue = new Money(marketValue, reportingCurrency);
                snap.CostBasis = new Money(costBasis, reportingCurrency);
                snap.RealizedPnL = new Money(realizedTotal, reportingCurrency);
                snap.UnrealizedPnL = new Money(marketValue - costBasis, reportingCurrency);
                updated++;
            }
            else
            {
                _db.PortfolioValueSnapshots.Add(new PortfolioValueSnapshot
                {
                    SubPortfolioId = subPortfolioId,
                    Date = date,
                    MarketValue = new Money(marketValue, reportingCurrency),
                    CostBasis = new Money(costBasis, reportingCurrency),
                    RealizedPnL = new Money(realizedTotal, reportingCurrency),
                    UnrealizedPnL = new Money(marketValue - costBasis, reportingCurrency)
                });
                created++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return new SnapshotBackfillResult(start, end, created, updated, symbols.Count, missingSymbols);
    }

    private static decimal? FindCloseOnOrBefore(SortedList<DateOnly, decimal>? list, DateOnly date)
    {
        if (list is null || list.Count == 0) return null;
        if (list.TryGetValue(date, out var exact)) return exact;
        // O gün yoksa (tatil vb.) önceki en yakın kapanışı taşı.
        decimal? best = null;
        foreach (var (d, c) in list)
        {
            if (d > date) break;
            best = c;
        }
        return best;
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

        // Nakit hiçbir zaman eksiye düşmez; yuvarlama artıkları 0'a kelepçelenir.
        return new Money(Math.Max(0m, balance), reportingCurrency);
    }
}
