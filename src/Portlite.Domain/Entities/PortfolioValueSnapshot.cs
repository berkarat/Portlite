using Portlite.Domain.Common;

namespace Portlite.Domain.Entities;

public class PortfolioValueSnapshot : BaseEntity
{
    public Guid SubPortfolioId { get; set; }
    public DateOnly Date { get; set; }
    public Money MarketValue { get; set; }
    public Money CostBasis { get; set; }
    public Money RealizedPnL { get; set; }
    public Money UnrealizedPnL { get; set; }

    public SubPortfolio SubPortfolio { get; set; } = null!;
}
