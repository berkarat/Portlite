namespace Portlite.Shared.Dtos;

public record PortfolioAnalysisDto(
    Guid Id,
    Guid SubPortfolioId,
    DateTime GeneratedAt,
    string Summary,
    List<AnalysisWarningDto> Warnings,
    List<AnalysisSuggestionDto> Suggestions,
    string MarketContext,
    bool FromCache);

public record AnalysisWarningDto(string Severity, string Title, string Detail);

public record AnalysisSuggestionDto(string Priority, string Action, string Reasoning);

// AI'dan parse edilen ham yanıt — Severity/Priority validate edildikten sonra DTO'ya dönüştürülür
public record AnalysisResultRaw(
    string Summary,
    List<AnalysisWarningDto> Warnings,
    List<AnalysisSuggestionDto> Suggestions,
    string MarketContext);
