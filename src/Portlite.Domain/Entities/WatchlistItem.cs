using Portlite.Domain.Common;

namespace Portlite.Domain.Entities;

public class WatchlistItem : BaseEntity
{
    public string AssetSymbol { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int DisplayOrder { get; set; }

    public Asset Asset { get; set; } = null!;
}
