using Portlite.Domain.Enums;

namespace Portlite.Domain.Entities;

public class OptionDetail
{
    public string UnderlyingSymbol { get; set; } = string.Empty;
    public OptionType OptionType { get; set; }
    public decimal Strike { get; set; }
    public DateOnly Expiry { get; set; }
    public int Multiplier { get; set; } = 100;
}
