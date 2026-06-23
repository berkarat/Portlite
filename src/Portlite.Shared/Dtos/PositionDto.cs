using Portlite.Domain.Enums;

namespace Portlite.Shared.Dtos;

public record PositionDto(
    Guid SubPortfolioId,
    string AssetSymbol,
    string AssetName,
    AssetType AssetType,
    CurrencyCode Currency,
    decimal Quantity,
    decimal AverageCost,
    decimal TotalCost,
    decimal RealizedPnL,
    int TradeCount,
    DateTime? FirstTradeAt,
    DateTime? LastTradeAt,
    decimal? CurrentPrice,
    decimal? MarketValue,
    decimal? UnrealizedPnL,
    DateOnly? PriceAsOf,
    decimal? PreviousClose,
    decimal? DayChange,
    decimal? DayChangePercent,
    string? Theme = null);

public record SetCostBasisRequest(decimal AverageCost);
