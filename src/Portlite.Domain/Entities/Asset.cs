using Portlite.Domain.Common;
using Portlite.Domain.Enums;

namespace Portlite.Domain.Entities;

public class Asset : BaseEntity
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AssetType Type { get; set; }
    public CurrencyCode Currency { get; set; }
    public string? Theme { get; set; }
    public OptionDetail? OptionDetail { get; set; }

    public ICollection<Trade> Trades { get; set; } = new List<Trade>();
    public ICollection<PriceSnapshot> PriceSnapshots { get; set; } = new List<PriceSnapshot>();
}
