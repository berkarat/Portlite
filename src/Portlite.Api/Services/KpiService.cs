using Microsoft.EntityFrameworkCore;
using Portlite.Api.Mappings;
using Portlite.Domain.Common;
using Portlite.Domain.Entities;
using Portlite.Domain.Enums;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class KpiService
{
    private readonly PortliteDbContext _db;
    private readonly PortfolioSnapshotService _snapshots;

    public KpiService(PortliteDbContext db, PortfolioSnapshotService snapshots)
    {
        _db = db;
        _snapshots = snapshots;
    }

    public async Task<KpiSummaryDto> GetAsync(Guid subPortfolioId, CancellationToken ct = default)
    {
        var portfolioExists = await _db.SubPortfolios.AnyAsync(x => x.Id == subPortfolioId, ct);
        if (!portfolioExists) throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        var reportingCurrency = CurrencyCode.USD;

        var snapshots = await _db.PortfolioValueSnapshots
            .Where(s => s.SubPortfolioId == subPortfolioId)
            .OrderBy(s => s.Date)
            .ToListAsync(ct);

        if (snapshots.Count == 0)
        {
            var zero = new Money(0m, reportingCurrency).ToDto();
            return new KpiSummaryDto(
                subPortfolioId,
                DateOnly.FromDateTime(DateTime.UtcNow),
                zero, zero, zero, zero, null, zero, null, zero, null,
                zero,
                HasBaseline: false);
        }

        var latest = snapshots[^1];
        var asOf = latest.Date;

        var cashBalance = await _snapshots.CalculateCashBalanceAsync(subPortfolioId, reportingCurrency, ct);
        var totalEquity = new Money(latest.MarketValue.Amount + cashBalance.Amount, reportingCurrency);

        var netCash = await NetCashContributedAsync(subPortfolioId, reportingCurrency, ct);

        // All-time PnL = TotalEquity - NetCashContributed
        var allTimePnLAmount = totalEquity.Amount - netCash.Amount;
        decimal? allTimePct = netCash.Amount != 0
            ? allTimePnLAmount / netCash.Amount * 100m
            : null;

        // Last day change = latest equity - previous-snapshot equity (tarihe duyarlı nakit + akış arındırma)
        var prevDay = snapshots.LastOrDefault(s => s.Date < latest.Date);
        var lastDayChange = decimal.Zero;
        decimal? lastDayPct = null;
        if (prevDay is not null)
        {
            var prevCash = await _snapshots.CalculateCashBalanceAsync(subPortfolioId, reportingCurrency, ct, asOf: prevDay.Date);
            var prevEquity = prevDay.MarketValue.Amount + prevCash.Amount;
            var dayFlow = await NetFlowBetweenAsync(subPortfolioId, reportingCurrency, prevDay.Date, latest.Date, ct);
            lastDayChange = totalEquity.Amount - dayFlow - prevEquity;
            lastDayPct = prevEquity != 0 ? lastDayChange / prevEquity * 100m : null;
        }

        // YTD: yıl başı özkaynak (o günkü nakitle) baz alınır; yıl içi net para yatırma kâr sayılmaz.
        var jan1 = new DateOnly(latest.Date.Year, 1, 1);
        var startOfYear = snapshots.LastOrDefault(s => s.Date <= jan1) ?? snapshots.FirstOrDefault(s => s.Date >= jan1);
        decimal ytdChange = 0m;
        decimal? ytdPct = null;
        if (startOfYear is not null && startOfYear.Date != latest.Date)
        {
            var soyCash = await _snapshots.CalculateCashBalanceAsync(subPortfolioId, reportingCurrency, ct, asOf: startOfYear.Date);
            var soyEquity = startOfYear.MarketValue.Amount + soyCash.Amount;
            var ytdFlow = await NetFlowBetweenAsync(subPortfolioId, reportingCurrency, startOfYear.Date, latest.Date, ct);
            ytdChange = totalEquity.Amount - ytdFlow - soyEquity;
            var investedBase = soyEquity + Math.Max(0m, ytdFlow);
            ytdPct = investedBase > 0 ? ytdChange / investedBase * 100m : null;
        }

        return new KpiSummaryDto(
            subPortfolioId,
            asOf,
            totalEquity.ToDto(),
            cashBalance.ToDto(),
            latest.MarketValue.ToDto(),
            new Money(allTimePnLAmount, reportingCurrency).ToDto(),
            allTimePct,
            new Money(lastDayChange, reportingCurrency).ToDto(),
            lastDayPct,
            new Money(ytdChange, reportingCurrency).ToDto(),
            ytdPct,
            netCash.ToDto(),
            HasBaseline: snapshots.Count > 1);
    }

    // (afterExclusive, throughInclusive] aralığındaki net dış para akışı (+Deposit, −Withdraw).
    private async Task<decimal> NetFlowBetweenAsync(
        Guid subPortfolioId,
        CurrencyCode reportingCurrency,
        DateOnly afterExclusive,
        DateOnly throughInclusive,
        CancellationToken ct)
    {
        var fromCutoff = afterExclusive.ToDateTime(TimeOnly.MaxValue);
        var toCutoff = throughInclusive.ToDateTime(TimeOnly.MaxValue);
        var txs = await _db.CashTransactions
            .Where(c => c.SubPortfolioId == subPortfolioId
                && (c.Type == CashTxType.Deposit || c.Type == CashTxType.Withdraw)
                && c.OccurredAt > fromCutoff && c.OccurredAt <= toCutoff)
            .ToListAsync(ct);
        return txs
            .Where(t => t.Amount.Currency == reportingCurrency)
            .Sum(t => t.Type == CashTxType.Deposit ? t.Amount.Amount : -t.Amount.Amount);
    }

    private async Task<Money> NetCashContributedAsync(
        Guid subPortfolioId,
        CurrencyCode reportingCurrency,
        CancellationToken ct)
    {
        var txs = await _db.CashTransactions
            .Where(c => c.SubPortfolioId == subPortfolioId
                && (c.Type == CashTxType.Deposit || c.Type == CashTxType.Withdraw))
            .ToListAsync(ct);

        decimal sum = 0m;
        foreach (var t in txs)
        {
            if (t.Amount.Currency != reportingCurrency) continue;
            sum += t.Type == CashTxType.Deposit ? t.Amount.Amount : -t.Amount.Amount;
        }
        return new Money(sum, reportingCurrency);
    }
}
