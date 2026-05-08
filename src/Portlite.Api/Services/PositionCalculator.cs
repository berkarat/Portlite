using Microsoft.EntityFrameworkCore;
using Portlite.Domain.Entities;
using Portlite.Domain.Enums;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class PositionCalculator
{
    private readonly PortliteDbContext _db;
    private readonly PriceSnapshotStore _priceStore;

    public PositionCalculator(PortliteDbContext db, PriceSnapshotStore priceStore)
    {
        _db = db;
        _priceStore = priceStore;
    }

    public async Task<List<PositionDto>> CalculateForPortfolioAsync(Guid subPortfolioId, CancellationToken ct = default)
    {
        var portfolioExists = await _db.SubPortfolios.AnyAsync(x => x.Id == subPortfolioId, ct);
        if (!portfolioExists) throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        var trades = await _db.Trades
            .Where(t => t.SubPortfolioId == subPortfolioId)
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync(ct);

        if (trades.Count == 0) return new List<PositionDto>();

        var symbols = trades.Select(t => t.AssetSymbol).Distinct().ToList();
        var assets = await _db.Assets
            .Where(a => symbols.Contains(a.Symbol))
            .ToDictionaryAsync(a => a.Symbol, ct);

        var positions = new List<PositionDto>();
        foreach (var group in trades.GroupBy(t => t.AssetSymbol))
        {
            if (!assets.TryGetValue(group.Key, out var asset)) continue;
            var basePos = BuildPosition(subPortfolioId, asset, group.OrderBy(t => t.ExecutedAt).ToList());
            var enriched = await EnrichWithPriceAsync(basePos, asset, ct);
            positions.Add(enriched);
        }

        return positions
            .Where(p => p.Quantity != 0 || p.RealizedPnL != 0)
            .OrderBy(p => p.AssetSymbol)
            .ToList();
    }

    private async Task<PositionDto> EnrichWithPriceAsync(PositionDto pos, Asset asset, CancellationToken ct)
    {
        if (pos.Quantity <= 0) return pos;
        var snap = await _priceStore.GetLatestAsync(pos.AssetSymbol, ct);
        if (snap is null) return pos;

        var multiplier = asset.Type == AssetType.Option
            ? (asset.OptionDetail?.Multiplier ?? 100m)
            : 1m;
        var marketValue = pos.Quantity * snap.Close * multiplier;
        var unrealized = marketValue - pos.TotalCost;

        decimal? dayChange = null;
        decimal? dayChangePct = null;
        if (snap.PreviousClose.HasValue && snap.PreviousClose.Value > 0)
        {
            dayChange = pos.Quantity * (snap.Close - snap.PreviousClose.Value) * multiplier;
            dayChangePct = (snap.Close - snap.PreviousClose.Value) / snap.PreviousClose.Value * 100m;
        }

        return pos with
        {
            CurrentPrice = snap.Close,
            MarketValue = marketValue,
            UnrealizedPnL = unrealized,
            PriceAsOf = snap.Date,
            PreviousClose = snap.PreviousClose,
            DayChange = dayChange,
            DayChangePercent = dayChangePct
        };
    }

    private static PositionDto BuildPosition(Guid subPortfolioId, Asset asset, List<Trade> orderedTrades)
    {
        decimal qty = 0m;
        decimal totalCost = 0m;
        decimal realizedPnL = 0m;

        foreach (var t in orderedTrades)
        {
            if (t.Side == TradeSide.Buy)
            {
                totalCost += t.Quantity * t.Price + t.Fee;
                qty += t.Quantity;
            }
            else // Sell
            {
                var avg = qty > 0 ? totalCost / qty : 0m;
                var soldQty = Math.Min(t.Quantity, qty);
                realizedPnL += soldQty * (t.Price - avg) - t.Fee;
                totalCost -= avg * soldQty;
                qty -= t.Quantity;
            }
        }

        var avgCost = qty > 0 ? totalCost / qty : 0m;

        return new PositionDto(
            subPortfolioId,
            asset.Symbol,
            asset.Name,
            asset.Type,
            asset.Currency,
            qty,
            avgCost,
            qty > 0 ? totalCost : 0m,
            realizedPnL,
            orderedTrades.Count,
            orderedTrades.First().ExecutedAt,
            orderedTrades.Last().ExecutedAt,
            CurrentPrice: null,
            MarketValue: null,
            UnrealizedPnL: null,
            PriceAsOf: null,
            PreviousClose: null,
            DayChange: null,
            DayChangePercent: null);
    }
}
