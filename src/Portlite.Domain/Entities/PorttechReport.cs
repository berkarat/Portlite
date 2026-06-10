using Portlite.Domain.Common;

namespace Portlite.Domain.Entities;

public class PorttechReport : BaseEntity
{
    public Guid SubPortfolioId { get; set; }
    public DateOnly ReportDate { get; set; }
    public string TechnicalDataJson { get; set; } = string.Empty;  // Serialized list of TechnicalIndicators
    public string ReportJson { get; set; } = string.Empty;         // AI-generated report
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    public SubPortfolio SubPortfolio { get; set; } = null!;
}
