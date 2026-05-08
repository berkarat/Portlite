using System.Globalization;
using Portlite.Domain.Enums;
using Portlite.Shared.Dtos;

namespace Portlite.Web.Api;

public static class Format
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Money(MoneyDto m) =>
        $"{Sign(m.Currency)}{m.Amount.ToString("N2", Inv)}";

    public static string MoneyNoSign(MoneyDto m) =>
        m.Amount.ToString("N2", Inv);

    public static string Signed(decimal value, CurrencyCode currency)
    {
        var prefix = value > 0 ? "+" : "";
        return $"{prefix}{Sign(currency)}{value.ToString("N2", Inv)}";
    }

    public static string SignedPct(decimal? pct)
    {
        if (!pct.HasValue) return "—";
        var prefix = pct.Value > 0 ? "+" : "";
        return $"{prefix}{pct.Value.ToString("N2", Inv)}%";
    }

    public static string Sign(CurrencyCode c) => c switch
    {
        CurrencyCode.USD => "$",
        CurrencyCode.TRY => "₺",
        _ => ""
    };

    public static string DeltaClass(decimal value) =>
        value > 0 ? "tag-pos" : value < 0 ? "tag-neg" : "tag-flat";

    public static string DeltaClassPct(decimal? pct) =>
        !pct.HasValue ? "tag-flat" : DeltaClass(pct.Value);

    public static string Qty(decimal q) => q.ToString("N4", Inv).TrimEnd('0').TrimEnd('.');

    public static string ShortDate(DateTime dt) => dt.ToLocalTime().ToString("dd MMM yyyy", new CultureInfo("tr-TR"));
    public static string ShortDate(DateOnly d) => d.ToString("dd MMM yyyy", new CultureInfo("tr-TR"));
    public static string Time(DateTime dt) => dt.ToLocalTime().ToString("HH:mm");
}
