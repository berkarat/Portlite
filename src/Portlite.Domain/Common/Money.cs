using Portlite.Domain.Enums;

namespace Portlite.Domain.Common;

public readonly record struct Money(decimal Amount, CurrencyCode Currency)
{
    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    public override string ToString() => $"{Amount:0.##} {Currency}";
}
