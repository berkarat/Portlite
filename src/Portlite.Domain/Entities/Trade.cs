using Portlite.Domain.Common;
using Portlite.Domain.Enums;

namespace Portlite.Domain.Entities;

public class Trade : BaseEntity
{
    public Guid SubPortfolioId { get; set; }
    public string AssetSymbol { get; set; } = string.Empty;
    public TradeSide Side { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Fee { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string? Notes { get; set; }

    public SubPortfolio SubPortfolio { get; set; } = null!;
    public Asset Asset { get; set; } = null!;
}
