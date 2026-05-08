using Portlite.Domain.Enums;

namespace Portlite.Shared.Dtos;

public record MoneyDto(decimal Amount, CurrencyCode Currency);

public record OptionDetailDto(
    string UnderlyingSymbol,
    OptionType OptionType,
    decimal Strike,
    DateOnly Expiry,
    int Multiplier);
