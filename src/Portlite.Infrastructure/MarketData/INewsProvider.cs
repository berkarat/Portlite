namespace Portlite.Infrastructure.MarketData;

public interface INewsProvider
{
    Task<List<NewsItem>> GetCompanyNewsAsync(string symbol, int days, CancellationToken ct = default);
}

public record NewsItem(
    long Id,
    string Symbol,
    string Headline,
    string Summary,
    string Source,
    string Url,
    string? ImageUrl,
    DateTime PublishedAt,
    string Category);
