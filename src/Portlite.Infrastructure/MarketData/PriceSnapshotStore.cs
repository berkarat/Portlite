using Microsoft.EntityFrameworkCore;
using Portlite.Domain.Entities;
using Portlite.Infrastructure.Persistence;

namespace Portlite.Infrastructure.MarketData;

public class PriceSnapshotStore
{
    private readonly PortliteDbContext _db;

    public PriceSnapshotStore(PortliteDbContext db) => _db = db;

    public async Task SaveAsync(QuoteResult quote, CancellationToken ct = default)
    {
        var date = DateOnly.FromDateTime(quote.Timestamp);

        var existing = await _db.PriceSnapshots
            .FirstOrDefaultAsync(p => p.AssetSymbol == quote.Symbol && p.Date == date, ct);

        if (existing is null)
        {
            _db.PriceSnapshots.Add(new PriceSnapshot
            {
                AssetSymbol = quote.Symbol,
                Date = date,
                Close = quote.Current,
                PreviousClose = quote.PreviousClose,
                Open = quote.DayOpen,
                High = quote.DayHigh,
                Low = quote.DayLow,
                Source = quote.Source
            });
        }
        else
        {
            existing.Close = quote.Current;
            existing.PreviousClose = quote.PreviousClose;
            existing.Open = quote.DayOpen ?? existing.Open;
            existing.High = quote.DayHigh ?? existing.High;
            existing.Low = quote.DayLow ?? existing.Low;
            existing.Source = quote.Source;
        }

        await _db.SaveChangesAsync(ct);
    }

    public Task<PriceSnapshot?> GetLatestAsync(string symbol, CancellationToken ct = default) =>
        _db.PriceSnapshots
            .Where(p => p.AssetSymbol == symbol)
            .OrderByDescending(p => p.Date)
            .FirstOrDefaultAsync(ct);

    public Task<PriceSnapshot?> GetOnOrBeforeAsync(string symbol, DateOnly date, CancellationToken ct = default) =>
        _db.PriceSnapshots
            .Where(p => p.AssetSymbol == symbol && p.Date <= date)
            .OrderByDescending(p => p.Date)
            .FirstOrDefaultAsync(ct);
}
