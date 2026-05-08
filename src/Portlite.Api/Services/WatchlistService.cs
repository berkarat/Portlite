using Microsoft.EntityFrameworkCore;
using Portlite.Domain.Entities;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class WatchlistService
{
    private readonly PortliteDbContext _db;
    private readonly AssetService _assets;
    private readonly IPriceProvider _prices;
    private readonly PriceSnapshotStore _priceStore;

    public WatchlistService(
        PortliteDbContext db,
        AssetService assets,
        IPriceProvider prices,
        PriceSnapshotStore priceStore)
    {
        _db = db;
        _assets = assets;
        _prices = prices;
        _priceStore = priceStore;
    }

    public async Task<List<WatchlistItemDto>> ListAsync(CancellationToken ct = default)
    {
        var items = await _db.WatchlistItems
            .Include(w => w.Asset)
            .OrderBy(w => w.DisplayOrder).ThenBy(w => w.AssetSymbol)
            .ToListAsync(ct);

        var results = new List<WatchlistItemDto>();
        foreach (var w in items)
            results.Add(await BuildDtoAsync(w, ct));
        return results;
    }

    public async Task<WatchlistItemDto> AddAsync(AddWatchlistRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Symbol))
            throw new ValidationException("Symbol is required.");

        // Ensure asset exists in catalog (lookup or create from Finnhub)
        var asset = await _assets.UpsertFromSymbolAsync(req.Symbol, ct);

        var existing = await _db.WatchlistItems
            .FirstOrDefaultAsync(w => w.AssetSymbol == asset.Symbol, ct);
        if (existing is not null)
            throw new ConflictException($"'{asset.Symbol}' watchlist'te zaten var.");

        var maxOrder = await _db.WatchlistItems.MaxAsync(w => (int?)w.DisplayOrder, ct) ?? 0;
        var item = new WatchlistItem
        {
            AssetSymbol = asset.Symbol,
            Notes = req.Notes,
            DisplayOrder = maxOrder + 1
        };
        _db.WatchlistItems.Add(item);
        await _db.SaveChangesAsync(ct);

        // Load fresh quote so the row shows immediately
        try
        {
            var quote = await _prices.GetQuoteAsync(asset.Symbol, ct);
            await _priceStore.SaveAsync(quote, ct);
        }
        catch { /* fail silently — UI shows price next refresh */ }

        var saved = await _db.WatchlistItems
            .Include(w => w.Asset)
            .FirstAsync(w => w.Id == item.Id, ct);
        return await BuildDtoAsync(saved, ct);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var item = await _db.WatchlistItems.FindAsync([id], ct)
            ?? throw new NotFoundException($"Watchlist item {id} not found.");
        _db.WatchlistItems.Remove(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<WatchlistItemDto>> RefreshAllPricesAsync(CancellationToken ct = default)
    {
        var items = await _db.WatchlistItems.Include(w => w.Asset).ToListAsync(ct);
        foreach (var w in items)
        {
            try
            {
                var quote = await _prices.GetQuoteAsync(w.AssetSymbol, ct);
                await _priceStore.SaveAsync(quote, ct);
            }
            catch { /* skip on failure */ }
        }
        return await ListAsync(ct);
    }

    private async Task<WatchlistItemDto> BuildDtoAsync(WatchlistItem w, CancellationToken ct)
    {
        var snap = await _priceStore.GetLatestAsync(w.AssetSymbol, ct);
        decimal? change = null, changePct = null;
        if (snap is not null && snap.PreviousClose.HasValue && snap.PreviousClose.Value > 0)
        {
            change = snap.Close - snap.PreviousClose.Value;
            changePct = change / snap.PreviousClose.Value * 100m;
        }
        return new WatchlistItemDto(
            w.Id,
            w.AssetSymbol,
            w.Asset.Name,
            w.Asset.Type,
            w.Asset.Currency,
            w.Notes,
            w.DisplayOrder,
            snap?.Close,
            snap?.PreviousClose,
            change,
            changePct,
            snap is null ? null : DateTime.SpecifyKind(snap.UpdatedAt, DateTimeKind.Utc),
            w.CreatedAt);
    }
}
