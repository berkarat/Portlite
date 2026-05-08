using Portlite.Domain.Common;

namespace Portlite.Domain.Entities;

public class PortfolioAnalysis : BaseEntity
{
    public const int ContentHashLength = 64; // SHA-256 hex length

    public Guid SubPortfolioId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    public SubPortfolio SubPortfolio { get; set; } = null!;
}
