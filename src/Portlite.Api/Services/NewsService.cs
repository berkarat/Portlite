using Microsoft.EntityFrameworkCore;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class NewsService
{
    private readonly PortliteDbContext _db;
    private readonly PositionCalculator _positions;
    private readonly INewsProvider _news;
    private readonly ILogger<NewsService> _log;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(20);
    private static readonly Dictionary<string, (DateTime fetchedAt, List<NewsItem> items)> _memCache = new();
    private static readonly object _cacheLock = new();

    public NewsService(
        PortliteDbContext db,
        PositionCalculator positions,
        INewsProvider news,
        ILogger<NewsService> log)
    {
        _db = db;
        _positions = positions;
        _news = news;
        _log = log;
    }

    public async Task<List<NewsItemDto>> GetForSymbolAsync(string symbol, int days, int take, CancellationToken ct)
    {
        var items = await FetchSymbolAsync(symbol, days, ct);
        return items.Take(take).Select(MapDto).ToList();
    }

    public async Task<List<NewsItemDto>> GetForPortfolioAsync(Guid portfolioId, int days, int take, CancellationToken ct)
    {
        var portfolioExists = await _db.SubPortfolios.AnyAsync(p => p.Id == portfolioId, ct);
        if (!portfolioExists) throw new NotFoundException($"SubPortfolio {portfolioId} not found.");

        var positions = await _positions.CalculateForPortfolioAsync(portfolioId, ct);
        var symbols = positions
            .Where(p => p.Quantity > 0)
            .Select(p => p.AssetSymbol)
            .Distinct()
            .ToList();

        if (symbols.Count == 0) return new List<NewsItemDto>();

        // Parallel fetch — Finnhub has 60 req/min limit, 12 symbols is well under
        var tasks = symbols.Select(s => FetchSymbolAsync(s, days, ct)).ToArray();
        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(r => r)
            .GroupBy(n => n.Id)               // dedupe across symbols
            .Select(g => g.First())
            .OrderByDescending(n => n.PublishedAt)
            .Take(take)
            .Select(MapDto)
            .ToList();
    }

    private async Task<List<NewsItem>> FetchSymbolAsync(string symbol, int days, CancellationToken ct)
    {
        var key = $"{symbol}|{days}";
        lock (_cacheLock)
        {
            if (_memCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.fetchedAt < CacheTtl)
                return hit.items;
        }

        try
        {
            var items = await _news.GetCompanyNewsAsync(symbol, days, ct);
            lock (_cacheLock) { _memCache[key] = (DateTime.UtcNow, items); }
            return items;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "News fetch failed for {Symbol}", symbol);
            return new List<NewsItem>();
        }
    }

    private static NewsItemDto MapDto(NewsItem n) => new(
        n.Id, n.Symbol, n.Headline, n.Summary, n.Source, n.Url, n.ImageUrl, n.PublishedAt, n.Category);
}
