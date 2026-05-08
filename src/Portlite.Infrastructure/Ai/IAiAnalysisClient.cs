namespace Portlite.Infrastructure.Ai;

public interface IAiAnalysisClient
{
    Task<AiCompletionResult> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);
}

public record AiCompletionResult(string Content, int InputTokens, int OutputTokens);
