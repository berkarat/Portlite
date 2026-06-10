namespace Portlite.Shared.Dtos;

public class PorttechReportDto
{
    public Guid Id { get; set; }
    public DateOnly ReportDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string TechnicalDataJson { get; set; } = string.Empty;
    public string ReportJson { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}
