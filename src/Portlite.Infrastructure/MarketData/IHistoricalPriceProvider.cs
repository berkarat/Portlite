namespace Portlite.Infrastructure.MarketData;

public interface IHistoricalPriceProvider
{
    Task<List<DailyBar>> GetDailyBarsAsync(string symbol, int days = 252, CancellationToken ct = default);
}

public record DailyBar(
    string Symbol,
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
