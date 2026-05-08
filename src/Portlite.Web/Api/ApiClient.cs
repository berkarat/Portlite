using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Portlite.Domain.Enums;
using Portlite.Shared.Dtos;

namespace Portlite.Web.Api;

public class ApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ApiClient(HttpClient http) => _http = http;

    // ---------------- portfolios ----------------
    public Task<List<SubPortfolioDto>?> ListPortfoliosAsync() =>
        _http.GetFromJsonAsync<List<SubPortfolioDto>>("api/portfolios", JsonOpts);

    public Task<SubPortfolioDto?> GetPortfolioAsync(Guid id) =>
        _http.GetFromJsonAsync<SubPortfolioDto>($"api/portfolios/{id}", JsonOpts);

    public async Task<SubPortfolioDto> CreatePortfolioAsync(CreateSubPortfolioRequest req)
    {
        var res = await _http.PostAsJsonAsync("api/portfolios", req, JsonOpts);
        await EnsureSuccess(res);
        return (await res.Content.ReadFromJsonAsync<SubPortfolioDto>(JsonOpts))!;
    }

    public async Task<SubPortfolioDto> UpdatePortfolioAsync(Guid id, UpdateSubPortfolioRequest req)
    {
        var res = await _http.PutAsJsonAsync($"api/portfolios/{id}", req, JsonOpts);
        await EnsureSuccess(res);
        return (await res.Content.ReadFromJsonAsync<SubPortfolioDto>(JsonOpts))!;
    }

    public async Task DeletePortfolioAsync(Guid id, bool force = false)
    {
        var url = $"api/portfolios/{id}" + (force ? "?force=true" : "");
        var res = await _http.DeleteAsync(url);
        await EnsureSuccess(res);
    }

    public Task<MoneyDto?> GetCashBalanceAsync(Guid portfolioId) =>
        _http.GetFromJsonAsync<MoneyDto>($"api/portfolios/{portfolioId}/cash-balance", JsonOpts);

    // ---------------- assets ----------------
    public Task<List<AssetDto>?> ListAssetsAsync(AssetType? type = null)
    {
        var q = type.HasValue ? $"?type={type}" : "";
        return _http.GetFromJsonAsync<List<AssetDto>>($"api/assets{q}", JsonOpts);
    }

    public Task<AssetDto?> GetAssetAsync(string symbol) =>
        _http.GetFromJsonAsync<AssetDto>($"api/assets/{Uri.EscapeDataString(symbol)}", JsonOpts);

    public async Task<AssetDto> CreateAssetAsync(CreateAssetRequest req)
    {
        var res = await _http.PostAsJsonAsync("api/assets", req, JsonOpts);
        await EnsureSuccess(res);
        return (await res.Content.ReadFromJsonAsync<AssetDto>(JsonOpts))!;
    }

    public Task<List<SymbolSearchHitDto>?> SearchSymbolsAsync(string query) =>
        _http.GetFromJsonAsync<List<SymbolSearchHitDto>>(
            $"api/assets/search?q={Uri.EscapeDataString(query)}", JsonOpts);

    public async Task<AssetDto> LookupAssetAsync(string symbol)
    {
        var res = await _http.PostAsJsonAsync("api/assets/lookup",
            new SymbolLookupRequest(symbol), JsonOpts);
        await EnsureSuccess(res);
        return (await res.Content.ReadFromJsonAsync<AssetDto>(JsonOpts))!;
    }

    // ---------------- trades ----------------
    public Task<List<TradeDto>?> ListTradesAsync(Guid portfolioId) =>
        _http.GetFromJsonAsync<List<TradeDto>>($"api/portfolios/{portfolioId}/trades", JsonOpts);

    public async Task<TradeDto> CreateTradeAsync(Guid portfolioId, CreateTradeRequest req)
    {
        var res = await _http.PostAsJsonAsync($"api/portfolios/{portfolioId}/trades", req, JsonOpts);
        await EnsureSuccess(res);
        return (await res.Content.ReadFromJsonAsync<TradeDto>(JsonOpts))!;
    }

    public async Task DeleteTradeAsync(Guid id)
    {
        var res = await _http.DeleteAsync($"api/trades/{id}");
        await EnsureSuccess(res);
    }

    // ---------------- cash ----------------
    public Task<List<CashTransactionDto>?> ListCashAsync(Guid portfolioId) =>
        _http.GetFromJsonAsync<List<CashTransactionDto>>($"api/portfolios/{portfolioId}/cash", JsonOpts);

    public async Task<CashTransactionDto> CreateCashAsync(Guid portfolioId, CreateCashTransactionRequest req)
    {
        var res = await _http.PostAsJsonAsync($"api/portfolios/{portfolioId}/cash", req, JsonOpts);
        await EnsureSuccess(res);
        return (await res.Content.ReadFromJsonAsync<CashTransactionDto>(JsonOpts))!;
    }

    public async Task DeleteCashAsync(Guid id)
    {
        var res = await _http.DeleteAsync($"api/cash/{id}");
        await EnsureSuccess(res);
    }

    // ---------------- positions / kpis / snapshots / quotes ----------------
    public Task<List<PositionDto>?> ListPositionsAsync(Guid portfolioId) =>
        _http.GetFromJsonAsync<List<PositionDto>>($"api/portfolios/{portfolioId}/positions", JsonOpts);

    public Task<KpiSummaryDto?> GetKpisAsync(Guid portfolioId) =>
        _http.GetFromJsonAsync<KpiSummaryDto>($"api/portfolios/{portfolioId}/kpis", JsonOpts);

    public Task<List<PortfolioSnapshotDto>?> ListSnapshotsAsync(Guid portfolioId) =>
        _http.GetFromJsonAsync<List<PortfolioSnapshotDto>>($"api/portfolios/{portfolioId}/snapshots", JsonOpts);

    public async Task<PortfolioSnapshotDto> CreateSnapshotAsync(Guid portfolioId)
    {
        var res = await _http.PostAsync($"api/portfolios/{portfolioId}/snapshot", null);
        await EnsureSuccess(res);
        return (await res.Content.ReadFromJsonAsync<PortfolioSnapshotDto>(JsonOpts))!;
    }

    public Task<QuoteDto?> GetQuoteAsync(string symbol) =>
        _http.GetFromJsonAsync<QuoteDto>($"api/assets/{Uri.EscapeDataString(symbol)}/quote", JsonOpts);

    public Task<List<PricePointDto>?> GetPriceHistoryAsync(string symbol) =>
        _http.GetFromJsonAsync<List<PricePointDto>>($"api/assets/{Uri.EscapeDataString(symbol)}/history", JsonOpts);

    // ---------------- watchlist ----------------
    public Task<List<WatchlistItemDto>?> ListWatchlistAsync() =>
        _http.GetFromJsonAsync<List<WatchlistItemDto>>("api/watchlist", JsonOpts);

    public async Task<WatchlistItemDto> AddWatchlistAsync(string symbol, string? notes)
    {
        var res = await _http.PostAsJsonAsync("api/watchlist",
            new AddWatchlistRequest(symbol, notes), JsonOpts);
        await EnsureSuccess(res);
        return (await res.Content.ReadFromJsonAsync<WatchlistItemDto>(JsonOpts))!;
    }

    public async Task RemoveWatchlistAsync(Guid id)
    {
        var res = await _http.DeleteAsync($"api/watchlist/{id}");
        await EnsureSuccess(res);
    }

    public async Task<List<WatchlistItemDto>> RefreshWatchlistAsync()
    {
        var res = await _http.PostAsync("api/watchlist/refresh", null);
        await EnsureSuccess(res);
        return (await res.Content.ReadFromJsonAsync<List<WatchlistItemDto>>(JsonOpts)) ?? new();
    }

    // ---------------- analysis ----------------
    public async Task<PortfolioAnalysisDto?> AnalyzePortfolioAsync(Guid portfolioId, bool forceRefresh = false)
    {
        var url = $"api/portfolios/{portfolioId}/analyze?forceRefresh={forceRefresh.ToString().ToLowerInvariant()}";
        var res = await _http.PostAsync(url, content: null);
        await EnsureSuccess(res);
        return await res.Content.ReadFromJsonAsync<PortfolioAnalysisDto>(JsonOpts);
    }

    public Task<List<PortfolioAnalysisDto>?> GetAnalysisHistoryAsync(Guid portfolioId, int take = 10) =>
        _http.GetFromJsonAsync<List<PortfolioAnalysisDto>>(
            $"api/portfolios/{portfolioId}/analyses?take={take}", JsonOpts);

    // ---------------- news ----------------
    public Task<List<NewsItemDto>?> GetPortfolioNewsAsync(Guid portfolioId, int days = 7, int take = 30) =>
        _http.GetFromJsonAsync<List<NewsItemDto>>(
            $"api/news/portfolio/{portfolioId}?days={days}&take={take}", JsonOpts);

    public Task<List<NewsItemDto>?> GetSymbolNewsAsync(string symbol, int days = 14, int take = 30) =>
        _http.GetFromJsonAsync<List<NewsItemDto>>(
            $"api/news/{Uri.EscapeDataString(symbol)}?days={days}&take={take}", JsonOpts);

    private static async Task EnsureSuccess(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        string? message = null;
        try
        {
            var problem = await res.Content.ReadFromJsonAsync<ProblemResponse>(JsonOpts);
            message = problem?.Detail ?? problem?.Title;
        }
        catch { /* not a problem details payload */ }
        throw new ApiException(res.StatusCode, message ?? res.ReasonPhrase ?? "API error");
    }

    private record ProblemResponse(string? Title, string? Detail, int? Status);
}

public class ApiException : Exception
{
    public System.Net.HttpStatusCode Status { get; }
    public ApiException(System.Net.HttpStatusCode status, string message) : base(message) => Status = status;
}
