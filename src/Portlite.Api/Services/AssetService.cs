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
            OptionDetail = req.OptionDetail?.ToDomain()
        };
        _db.Assets.Add(asset);
        await _db.SaveChangesAsync(ct);
        return asset.ToDto();
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
