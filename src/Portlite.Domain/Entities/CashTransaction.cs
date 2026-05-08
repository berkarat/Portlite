using Portlite.Domain.Common;
using Portlite.Domain.Enums;

namespace Portlite.Domain.Entities;

public class CashTransaction : BaseEntity
{
    public Guid SubPortfolioId { get; set; }
    public CashTxType Type { get; set; }
    public Money Amount { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }

    public SubPortfolio SubPortfolio { get; set; } = null!;
}
