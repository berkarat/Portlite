using Microsoft.EntityFrameworkCore;
using Portlite.Api.Mappings;
using Portlite.Domain.Entities;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class TradeService
{
    private readonly PortliteDbContext _db;

    public TradeService(PortliteDbContext db) => _db = db;

    public async Task<List<TradeDto>> ListByPortfolioAsync(Guid subPortfolioId, CancellationToken ct = default)
    {
        var portfolioExists = await _db.SubPortfolios.AnyAsync(x => x.Id == subPortfolioId, ct);
        if (!portfolioExists) throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        var trades = await _db.Trades
            .Where(x => x.SubPortfolioId == subPortfolioId)
            .OrderByDescending(x => x.ExecutedAt)
            .ToListAsync(ct);
        return trades.Select(x => x.ToDto()).ToList();
    }

    public async Task<TradeDto> CreateAsync(Guid subPortfolioId, CreateTradeRequest req, CancellationToken ct = default)
    {
        var portfolio = await _db.SubPortfolios.FindAsync([subPortfolioId], ct)
            ?? throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        if (string.IsNullOrWhiteSpace(req.AssetSymbol)) throw new ValidationException("AssetSymbol is required.");
        if (req.Quantity <= 0) throw new ValidationException("Quantity must be > 0.");
        if (req.Price < 0) throw new ValidationException("Price cannot be negative.");
        if (req.Fee < 0) throw new ValidationException("Fee cannot be negative.");

        var symbol = req.AssetSymbol.Trim();
        var assetExists = await _db.Assets.AnyAsync(x => x.Symbol == symbol, ct);
        if (!assetExists)
            throw new ValidationException($"Asset '{symbol}' is not registered. Create it first via /api/assets.");

        var trade = new Trade
        {
            SubPortfolioId = subPortfolioId,
            AssetSymbol = symbol,
            Side = req.Side,
            Quantity = req.Quantity,
            Price = req.Price,
            Fee = req.Fee,
            ExecutedAt = req.ExecutedAt,
            Notes = req.Notes
        };
        _db.Trades.Add(trade);
        await _db.SaveChangesAsync(ct);
        return trade.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var trade = await _db.Trades.FindAsync([id], ct)
            ?? throw new NotFoundException($"Trade {id} not found.");
        _db.Trades.Remove(trade);
        await _db.SaveChangesAsync(ct);
    }
}
