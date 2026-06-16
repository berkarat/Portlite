using Microsoft.EntityFrameworkCore;
using Portlite.Api.Mappings;
using Portlite.Domain.Entities;
using Portlite.Domain.Enums;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class AssetService
{
    private readonly PortliteDbContext _db;
    private readonly IPriceProvider _prices;

    public AssetService(PortliteDbContext db, IPriceProvider prices)
    {
        _db = db;
        _prices = prices;
    }

    public async Task<List<AssetDto>> ListAsync(AssetType? type, CancellationToken ct = default)
    {
        var query = _db.Assets.AsQueryable();
        if (type.HasValue) query = query.Where(x => x.Type == type.Value);
        var items = await query.OrderBy(x => x.Symbol).ToListAsync(ct);
        return items.Select(x => x.ToDto()).ToList();
    }

    public async Task<AssetDto> GetBySymbolAsync(string symbol, CancellationToken ct = default)
    {
        var a = await _db.Assets.FirstOrDefaultAsync(x => x.Symbol == symbol, ct)
            ?? throw new NotFoundException($"Asset '{symbol}' not found.");
        return a.ToDto();
    }

    public async Task<AssetDto> CreateAsync(CreateAssetRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Symbol)) throw new ValidationException("Symbol is required.");
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("Name is required.");
        if (req.Type == AssetType.Option && req.OptionDetail is null)
            throw new ValidationException("OptionDetail is required when Type=Option.");
        if (req.Type != AssetType.Option && req.OptionDetail is not null)
            throw new ValidationException("OptionDetail must be null unless Type=Option.");

        var symbol = req.Symbol.Trim();
        var exists = await _db.Assets.AnyAsync(x => x.Symbol == symbol, ct);
        if (exists) throw new ConflictException($"Asset '{symbol}' already exists.");

        var asset = new Asset
        {
            Symbol = symbol,
            Name = req.Name.Trim(),
            Type = req.Type,
            Currency = req.Currency,
            Theme = string.IsNullOrWhiteSpace(req.Theme) ? null : req.Theme.Trim(),
            OptionDetail = req.OptionDetail?.ToDomain()
        };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync(ct);
        return asset.ToDto();
    }

    public async Task<AssetDto> UpdateThemeAsync(string symbol, string? theme, CancellationToken ct = default)
    {
        var sym = symbol.Trim().ToUpperInvariant();
        var asset = await _db.Assets.FirstOrDefaultAsync(x => x.Symbol == sym, ct)
            ?? throw new NotFoundException($"Asset '{sym}' not found.");
        asset.Theme = string.IsNullOrWhiteSpace(theme) ? null : theme.Trim();
        await _db.SaveChangesAsync(ct);
        return asset.ToDto();
    }

    /// <summary>
    /// Fills the Theme field from Finnhub's industry classification for the given symbols.
    /// When overwrite=false, only assets without a theme are updated. Returns the number of assets changed.
    /// </summary>
    public async Task<int> AutoAssignThemesAsync(IEnumerable<string>? symbols, bool overwrite, CancellationToken ct = default)
    {
        var wanted = symbols?
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var query = _db.Assets.AsQueryable();
        if (wanted is { Count: > 0 })
            query = query.Where(a => wanted.Contains(a.Symbol));
        if (!overwrite)
            query = query.Where(a => a.Theme == null || a.Theme == "");

        var assets = await query.ToListAsync(ct);
        var changed = 0;

        foreach (var asset in assets)
        {
            var industry = await _prices.GetIndustryAsync(asset.Symbol, ct);
            if (string.IsNullOrWhiteSpace(industry)) continue;
            if (asset.Theme == industry) continue;
            asset.Theme = industry;
            changed++;

            // Finnhub ücretsiz katman hız limitine (429) takılmamak için sembol başına kısa bekleme.
            await Task.Delay(250, ct);
        }

        if (changed > 0) await _db.SaveChangesAsync(ct);
        return changed;
    }

    public async Task<List<SymbolSearchHit>> SearchSymbolsAsync(string query, CancellationToken ct = default) =>
        await _prices.SearchSymbolsAsync(query, ct);

    /// <summary>Look up a symbol via Finnhub and upsert it as a Stock asset (USD).</summary>
    public async Task<AssetDto> UpsertFromSymbolAsync(string symbol, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ValidationException("Symbol is required.");

        symbol = symbol.Trim().ToUpperInvariant();

        var existing = await _db.Assets.FirstOrDefaultAsync(a => a.Symbol == symbol, ct);
        if (existing is not null) return existing.ToDto();

        var hits = await _prices.SearchSymbolsAsync(symbol, ct);
        var match = hits.FirstOrDefault(h => string.Equals(h.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    ?? hits.FirstOrDefault();

        if (match is null)
            throw new ValidationException($"Symbol '{symbol}' not found in Finnhub catalog.");

        var asset = new Asset
        {
            Symbol = match.Symbol.ToUpperInvariant(),
            Name = string.IsNullOrWhiteSpace(match.Description) ? match.Symbol : match.Description,
            Type = AssetType.Stock,
            Currency = CurrencyCode.USD
        };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync(ct);
        return asset.ToDto();
    }
}
