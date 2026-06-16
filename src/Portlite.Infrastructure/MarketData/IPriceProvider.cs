namespace Portlite.Infrastructure.MarketData;

public interface IPriceProvider
{
    Task<QuoteResult> GetQuoteAsync(string symbol, CancellationToken ct = default);
    Task<List<SymbolSearchHit>> SearchSymbolsAsync(string query, CancellationToken ct = default);
    Task<string?> GetIndustryAsync(string symbol, CancellationToken ct = default);
}

public record QuoteResult(
    string Symbol,
    decimal Current,
    decimal PreviousClose,
    decimal? DayHigh,
    decimal? DayLow,
    decimal? DayOpen,
    DateTime Timestamp,
    string Source);

public record SymbolSearchHit(
    string Symbol,
    string DisplaySymbol,
    string Description,
    string Type);
