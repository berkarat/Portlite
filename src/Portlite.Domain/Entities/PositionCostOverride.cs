using Portlite.Domain.Common;

namespace Portlite.Domain.Entities;

// Bir pozisyonun ortalama maliyetini elle ezmek için (geçmiş işlemler korunur).
public class PositionCostOverride : BaseEntity
{
    public Guid SubPortfolioId { get; set; }
    public string AssetSymbol { get; set; } = string.Empty;
    public decimal AverageCost { get; set; }

    public SubPortfolio SubPortfolio { get; set; } = null!;
    public Asset Asset { get; set; } = null!;
}
