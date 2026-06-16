using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Portlite.Infrastructure.MarketData;

public class FinnhubPriceProvider : IPriceProvider, INewsProvider
{
    private readonly HttpClient _http;
    private readonly FinnhubOptions _options;

    public FinnhubPriceProvider(HttpClient http, IOptions<FinnhubOptions> options)
    {
        _http = http;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Finnhub:ApiKey is not configured.");
    }

    public async Task<QuoteResult> GetQuoteAsync(string symbol, CancellationToken ct = default)
    {
        var url = $"quote?symbol={Uri.EscapeDataString(symbol)}&token={_options.ApiKey}";
        var raw = await _http.GetFromJsonAsync<FinnhubQuoteResponse>(url, ct)
            ?? throw new InvalidOperationException($"Finnhub returned empty response for '{symbol}'.");

        if (raw.Current == 0 && raw.PreviousClose == 0 && raw.Timestamp == 0)
            throw new InvalidOperationException($"Finnhub has no data for symbol '{symbol}'.");

        return new QuoteResult(
            Symbol: symbol,
            Current: raw.Current,
            PreviousClose: raw.PreviousClose,
            DayHigh: raw.High,
            DayLow: raw.Low,
            DayOpen: raw.Open,
            Timestamp: DateTimeOffset.FromUnixTimeSeconds(raw.Timestamp).UtcDateTime,
            Source: "Finnhub");
    }

    public async Task<List<SymbolSearchHit>> SearchSymbolsAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<SymbolSearchHit>();

        var url = $"search?q={Uri.EscapeDataString(query)}&exchange=US&token={_options.ApiKey}";
        var raw = await _http.GetFromJsonAsync<FinnhubSearchResponse>(url, ct);

        if (raw?.Result is null) return new List<SymbolSearchHit>();

        return raw.Result
            .Where(r => !string.IsNullOrWhiteSpace(r.Symbol) && !r.Symbol.Contains('.'))
            .Select(r => new SymbolSearchHit(
                Symbol: r.Symbol ?? string.Empty,
                DisplaySymbol: r.DisplaySymbol ?? r.Symbol ?? string.Empty,
                Description: r.Description ?? string.Empty,
                Type: r.Type ?? string.Empty))
            .Take(10)
            .ToList();
    }

    public async Task<string?> GetIndustryAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        var url = $"stock/profile2?symbol={Uri.EscapeDataString(symbol)}&token={_options.ApiKey}";
        try
        {
            var raw = await _http.GetFromJsonAsync<FinnhubProfileResponse>(url, ct);
            return string.IsNullOrWhiteSpace(raw?.FinnhubIndustry) ? null : raw!.FinnhubIndustry;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<NewsItem>> GetCompanyNewsAsync(string symbol, int days, CancellationToken ct = default)
    {
        var to = DateTime.UtcNow.Date;
        var from = to.AddDays(-days);
        var url = $"company-news?symbol={Uri.EscapeDataString(symbol)}" +
                  $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&token={_options.ApiKey}";

        var raw = await _http.GetFromJsonAsync<List<FinnhubNewsItem>>(url, ct)
            ?? new List<FinnhubNewsItem>();

        return raw
            .Where(n => !string.IsNullOrWhiteSpace(n.Headline) && !string.IsNullOrWhiteSpace(n.Url))
            .Select(n => new NewsItem(
                Id: n.Id,
                Symbol: symbol,
                Headline: n.Headline ?? string.Empty,
                Summary: n.Summary ?? string.Empty,
                Source: n.Source ?? "Finnhub",
                Url: n.Url ?? string.Empty,
                ImageUrl: string.IsNullOrWhiteSpace(n.Image) ? null : n.Image,
                PublishedAt: DateTimeOffset.FromUnixTimeSeconds(n.Datetime).UtcDateTime,
                Category: n.Category ?? "company"))
            .OrderByDescending(n => n.PublishedAt)
            .ToList();
    }

    private record FinnhubNewsItem(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("datetime")] long Datetime,
        [property: JsonPropertyName("headline")] string? Headline,
        [property: JsonPropertyName("image")] string? Image,
        [property: JsonPropertyName("related")] string? Related,
        [property: JsonPropertyName("source")] string? Source,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("url")] string? Url);

    private record FinnhubSearchResponse(
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("result")] List<FinnhubSearchHit>? Result);

    private record FinnhubSearchHit(
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("displaySymbol")] string? DisplaySymbol,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("type")] string? Type);

    private record FinnhubQuoteResponse(
        [property: JsonPropertyName("c")] decimal Current,
        [property: JsonPropertyName("d")] decimal? Change,
        [property: JsonPropertyName("dp")] decimal? ChangePercent,
        [property: JsonPropertyName("h")] decimal? High,
        [property: JsonPropertyName("l")] decimal? Low,
        [property: JsonPropertyName("o")] decimal? Open,
        [property: JsonPropertyName("pc")] decimal PreviousClose,
        [property: JsonPropertyName("t")] long Timestamp);

    private record FinnhubProfileResponse(
        [property: JsonPropertyName("finnhubIndustry")] string? FinnhubIndustry,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("ticker")] string? Ticker);
}
