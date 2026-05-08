using Portlite.Domain.Enums;

namespace Portlite.Shared.Dtos;

public record QuoteDto(
    string Symbol,
    decimal Current,
    decimal PreviousClose,
    decimal Change,
    decimal ChangePercent,
    decimal? DayHigh,
    decimal? DayLow,
    decimal? DayOpen,
    DateTime Timestamp,
    CurrencyCode Currency,
    string Source);
