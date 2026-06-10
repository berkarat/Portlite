namespace Portlite.Infrastructure.MarketData;

public class FmpOptions
{
    public const string SectionName = "Fmp";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://financialmodelingprep.com/stable/";
}
