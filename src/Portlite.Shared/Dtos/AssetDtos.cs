using Portlite.Domain.Enums;

namespace Portlite.Shared.Dtos;

public record AssetDto(
    Guid Id,
    string Symbol,
    string Name,
    AssetType Type,
    CurrencyCode Currency,
    OptionDetailDto? OptionDetail);

public record CreateAssetRequest(
    string Symbol,
    string Name,
    AssetType Type,
    CurrencyCode Currency,
    OptionDetailDto? OptionDetail);

public record SymbolSearchHitDto(
    string Symbol,
    string DisplaySymbol,
    string Description,
    string Type);

public record SymbolLookupRequest(string Symbol);

public record PricePointDto(DateOnly Date, decimal Close);
