namespace Portlite.Infrastructure.Ai;

public class AzureFoundryOptions
{
    public const string SectionName = "AzureFoundry";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "anthropic-claude-sonnet-4-6";
    public int MaxOutputTokens { get; set; } = 16000;
    public int TimeoutSeconds { get; set; } = 60;
}
