namespace Portlite.Shared.Dtos;

public record KpiSummaryDto(
    Guid SubPortfolioId,
    DateOnly AsOf,
    MoneyDto TotalEquity,
    MoneyDto AllTimePnL,
    decimal? AllTimePnLPercent,
    MoneyDto LastDayChange,
    decimal? LastDayChangePercent,
    MoneyDto YtdChange,
    decimal? YtdChangePercent,
    MoneyDto NetCashContributed,
    bool HasBaseline);
