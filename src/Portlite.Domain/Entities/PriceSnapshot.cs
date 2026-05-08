using Portlite.Domain.Common;

namespace Portlite.Domain.Entities;

public class PriceSnapshot : BaseEntity
{
    public string AssetSymbol { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public decimal Close { get; set; }
    public decimal? PreviousClose { get; set; }
    public decimal? Open { get; set; }
    public decimal? High { get; set; }
    public decimal? Low { get; set; }
    public string Source { get; set; } = string.Empty;

    public Asset Asset { get; set; } = null!;
}
