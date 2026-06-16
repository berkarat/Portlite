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
        // gpt-5 / o1 / o3 / o4 ailesi "reasoning" modelleridir: max_tokens yerine
        // max_completion_tokens ister ve özel temperature kabul etmez.
        var isReasoningModel = IsReasoningModel(_opt.Model);

        var req = new ChatCompletionsOptions
        {
            Model = _opt.Model,
            ResponseFormat = ChatCompletionsResponseFormat.CreateJsonFormat(),
        };

        if (isReasoningModel)
        {
            // SDK MaxTokens'ı max_tokens olarak gönderir; reasoning modelleri reddeder.
            // Doğru parametreyi ham JSON olarak ekliyoruz.
            req.AdditionalProperties["max_completion_tokens"] =
                BinaryData.FromObjectAsJson(_opt.MaxOutputTokens);
            // Temperature set etmiyoruz (yalnızca varsayılanı kabul ederler).
        }
        else
        {
            req.MaxTokens = _opt.MaxOutputTokens;
            req.Temperature = 0.3f;
        }

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

    private static bool IsReasoningModel(string model)
    {
        var m = model.ToLowerInvariant();
        return m.StartsWith("gpt-5") || m.StartsWith("o1") || m.StartsWith("o3") || m.StartsWith("o4");
    }
}
