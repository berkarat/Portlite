using Portlite.Domain.Common;

namespace Portlite.Domain.Entities;

public class SubPortfolio : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Trade> Trades { get; set; } = new List<Trade>();
    public ICollection<CashTransaction> CashTransactions { get; set; } = new List<CashTransaction>();
    public ICollection<PortfolioValueSnapshot> ValueSnapshots { get; set; } = new List<PortfolioValueSnapshot>();
}
