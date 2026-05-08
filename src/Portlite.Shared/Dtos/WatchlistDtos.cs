using Portlite.Domain.Enums;

namespace Portlite.Shared.Dtos;

public record WatchlistItemDto(
    Guid Id,
    string Symbol,
    string Name,
    AssetType AssetType,
    CurrencyCode Currency,
    string? Notes,
    int DisplayOrder,
    decimal? CurrentPrice,
    decimal? PreviousClose,
    decimal? DayChange,
    decimal? DayChangePercent,
    DateTime? PriceAsOf,
    DateTime CreatedAt);

public record AddWatchlistRequest(string Symbol, string? Notes);
