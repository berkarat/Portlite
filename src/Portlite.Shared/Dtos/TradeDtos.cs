using Portlite.Domain.Enums;

namespace Portlite.Shared.Dtos;

public record TradeDto(
    Guid Id,
    Guid SubPortfolioId,
    string AssetSymbol,
    TradeSide Side,
    decimal Quantity,
    decimal Price,
    decimal Fee,
    DateTime ExecutedAt,
    string? Notes);

public record CreateTradeRequest(
    string AssetSymbol,
    TradeSide Side,
    decimal Quantity,
    decimal Price,
    decimal Fee,
    DateTime ExecutedAt,
    string? Notes);
