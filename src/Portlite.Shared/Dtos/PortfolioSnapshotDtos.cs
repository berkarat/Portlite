namespace Portlite.Shared.Dtos;

public record PortfolioSnapshotDto(
    Guid SubPortfolioId,
    DateOnly Date,
    MoneyDto MarketValue,
    MoneyDto CostBasis,
    MoneyDto RealizedPnL,
    MoneyDto UnrealizedPnL,
    MoneyDto CashBalance,
    MoneyDto TotalEquity,
    int PositionCount,
    List<string> MissingPriceSymbols);
