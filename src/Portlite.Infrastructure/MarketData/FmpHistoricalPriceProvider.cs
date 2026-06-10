using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Portlite.Infrastructure.MarketData;

public class FmpHistoricalPriceProvider : IHistoricalPriceProvider
{
    private readonly HttpClient _http;
    private readonly FmpOptions _options;

    public FmpHistoricalPriceProvider(HttpClient http, IOptions<FmpOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<List<DailyBar>> GetDailyBarsAsync(string symbol, int days = 252, CancellationToken ct = default)
    {
        // Try FMP first
        var fmpResult = await TryFmpAsync(symbol, days, ct);
        if (fmpResult is not null && fmpResult.Count >= 15)
            return fmpResult;

        // Fallback to Yahoo Finance (free, no key needed)
        return await GetFromYahooAsync(symbol, days, ct);
    }

    private async Task<List<DailyBar>?> TryFmpAsync(string symbol, int days, CancellationToken ct)
    {
        try
        {
            var url = $"historical-price-eod/full?symbol={Uri.EscapeDataString(symbol)}&apikey={_options.ApiKey}";
            var response = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), ct);
            if (!response.IsSuccessStatusCode) return null;

            var raw = await response.Content.ReadFromJsonAsync<List<FmpEodRow>>(ct) ?? new();
            if (raw.Count < 15) return null;

            return raw
                .OrderByDescending(r => r.Date)
                .Take(days)
                .Select(r => new DailyBar(
                    Symbol: symbol,
                    Date: DateOnly.Parse(r.Date),
                    Open: r.Open,
                    High: r.High,
                    Low: r.Low,
                    Close: r.Close,
                    Volume: r.Volume))
                .OrderBy(b => b.Date)
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<DailyBar>> GetFromYahooAsync(string symbol, int days, CancellationToken ct)
    {
        var period2 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var period1 = DateTimeOffset.UtcNow.AddDays(-days * 1.5).ToUnixTimeSeconds();

        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&period1={period1}&period2={period2}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0");

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return new();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var result = json.GetProperty("chart").GetProperty("result")[0];
        var timestamps = result.GetProperty("timestamp");
        var quotes = result.GetProperty("indicators").GetProperty("quote")[0];

        var bars = new List<DailyBar>();
        for (int i = 0; i < timestamps.GetArrayLength(); i++)
        {
            var close = quotes.GetProperty("close")[i];
            var open = quotes.GetProperty("open")[i];
            var high = quotes.GetProperty("high")[i];
            var low = quotes.GetProperty("low")[i];
            var vol = quotes.GetProperty("volume")[i];

            if (close.ValueKind == JsonValueKind.Null) continue;

            var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).UtcDateTime);
            bars.Add(new DailyBar(
                Symbol: symbol,
                Date: date,
                Open: open.GetDecimal(),
                High: high.GetDecimal(),
                Low: low.GetDecimal(),
                Close: close.GetDecimal(),
                Volume: vol.ValueKind == JsonValueKind.Null ? 0 : vol.GetInt64()));
        }

        return bars.OrderBy(b => b.Date).TakeLast(days).ToList();
    }

    private record FmpEodRow(
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("open")] decimal Open,
        [property: JsonPropertyName("high")] decimal High,
        [property: JsonPropertyName("low")] decimal Low,
        [property: JsonPropertyName("close")] decimal Close,
        [property: JsonPropertyName("volume")] long Volume);
}
