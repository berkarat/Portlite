using Azure;
using Azure.AI.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Portlite.Infrastructure.Ai;

public class AzureFoundryAnalysisClient : IAiAnalysisClient
{
    private readonly AzureFoundryOptions _opt;
    private readonly ILogger<AzureFoundryAnalysisClient> _log;
    private readonly ChatCompletionsClient _client;

    public AzureFoundryAnalysisClient(
        IOptions<AzureFoundryOptions> opt,
        ILogger<AzureFoundryAnalysisClient> log)
    {
        _opt = opt.Value;
        _log = log;

        if (string.IsNullOrWhiteSpace(_opt.Endpoint) || string.IsNullOrWhiteSpace(_opt.ApiKey))
            throw new InvalidOperationException(
                "AzureFoundry:Endpoint veya AzureFoundry:ApiKey ayarlanmamış. " +
                "user-secrets ile setle: dotnet user-secrets set \"AzureFoundry:Endpoint\" \"...\"");

        _client = new ChatCompletionsClient(
            new Uri(_opt.Endpoint),
            new AzureKeyCredential(_opt.ApiKey));
    }

    public async Task<AiCompletionResult> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default)
    {
        var req = new ChatCompletionsOptions
        {
            Model = _opt.Model,
            MaxTokens = _opt.MaxOutputTokens,
            Temperature = 0.3f,
            ResponseFormat = ChatCompletionsResponseFormat.CreateJsonFormat(),
        };
        req.Messages.Add(new ChatRequestSystemMessage(systemPrompt));
        req.Messages.Add(new ChatRequestUserMessage(userPrompt));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_opt.TimeoutSeconds));

        try
        {
            Response<ChatCompletions> resp = await _client.CompleteAsync(req, cts.Token);
            var content = resp.Value.Content ?? string.Empty;
            var usage = resp.Value.Usage;
            return new AiCompletionResult(content, usage?.PromptTokens ?? 0, usage?.CompletionTokens ?? 0);
        }
        catch (RequestFailedException ex)
        {
            _log.LogError(ex, "Azure Foundry call failed: {Status} {Message}", ex.Status, ex.Message);
            throw new InvalidOperationException(
                $"AI servis çağrısı başarısız (HTTP {ex.Status}). Endpoint/key/deployment'ı kontrol et.", ex);
        }
    }
}
