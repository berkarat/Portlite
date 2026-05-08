using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portlite.Domain.Common;
using Portlite.Domain.Entities;
using Portlite.Domain.Enums;
using Portlite.Infrastructure.Persistence;

namespace Portlite.Api.Controllers;

// Dev-only seeder for backfilling synthetic snapshot history so the dashboard
// chart and 30-day return KPI have data to render before real history accrues.
// Remove or guard with environment check before shipping.
[ApiController]
public class DevSeedController : ControllerBase
{
    private readonly PortliteDbContext _db;

    public DevSeedController(PortliteDbContext db) => _db = db;

    [HttpPost("api/dev/seed-history/{portfolioId:guid}")]
    public async Task<IActionResult> SeedHistory(
        Guid portfolioId,
        [FromQuery] int days = 60,
        CancellationToken ct = default)
    {
        var portfolio = await _db.SubPortfolios.FindAsync([portfolioId], ct);
        if (portfolio is null) return NotFound($"SubPortfolio {portfolioId} not found.");

        var trades = await _db.Trades
            .Where(t => t.SubPortfolioId == portfolioId)
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync(ct);

        if (trades.Count == 0) return BadRequest("No trades to seed against.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earliestTrade = DateOnly.FromDateTime(trades.Min(t => t.ExecutedAt));
        var startDate = today.AddDays(-days);
        if (startDate < earliestTrade) startDate = earliestTrade;

        var symbols = trades.Select(t => t.AssetSymbol).Distinct().ToList();

        // Ensure SPY benchmark asset exists
        if (!await _db.Assets.AnyAsync(a => a.Symbol == "SPY", ct))
        {
            _db.Assets.Add(new Asset
            {
                Symbol = "SPY",
                Name = "SPDR S&P 500 ETF Trust",
                Type = AssetType.Stock,
                Currency = CurrencyCode.USD
            });
            await _db.SaveChangesAsync(ct);
        }
        if (!symbols.Contains("SPY")) symbols.Add("SPY");

        // End-of-period prices: latest stored, otherwise plausible defaults
        var endPrices = new Dictionary<string, decimal>();
        var startPrices = new Dictionary<string, decimal>();
        foreach (var sym in symbols)
        {
            var latest = await _db.PriceSnapshots
                .Where(p => p.AssetSymbol == sym)
                .OrderByDescending(p => p.Date)
                .FirstOrDefaultAsync(ct);

            decimal endPrice = latest?.Close
                ?? sym switch { "SPY" => 740m, "NVDA" => 215m, _ => 200m };
            // Walk up ~15% over the period; gives an upward trend with noise
            decimal startPrice = endPrice * 0.85m;
            endPrices[sym] = endPrice;
            startPrices[sym] = startPrice;
        }

        var rng = new Random(42); // deterministic
        int snapshotsCreated = 0;
        int pricesCreated = 0;

        var totalDays = today.DayNumber - startDate.DayNumber;

        for (var date = startDate; date <= today; date = date.AddDays(1))
        {
            var progress = totalDays > 0
                ? (decimal)(date.DayNumber - startDate.DayNumber) / totalDays
                : 1m;

            var dayPrices = new Dictionary<string, decimal>();
            foreach (var sym in symbols)
            {
                var noise = (decimal)(rng.NextDouble() - 0.5) * 0.03m; // ±1.5%
                var p = startPrices[sym] + (endPrices[sym] - startPrices[sym]) * progress;
                p *= (1m + noise);
                dayPrices[sym] = Math.Round(p, 2);
            }

            // Price snapshots
            foreach (var (sym, price) in dayPrices)
            {
                var existing = await _db.PriceSnapshots
                    .FirstOrDefaultAsync(p => p.AssetSymbol == sym && p.Date == date, ct);
                if (existing is null)
                {
                    _db.PriceSnapshots.Add(new PriceSnapshot
                    {
                        AssetSymbol = sym,
                        Date = date,
                        Close = price,
                        Source = "dev-seed"
                    });
                    pricesCreated++;
                }
                else
                {
                    existing.Close = price;
                }
            }

            // Aggregate trades up to this date for this portfolio's positions
            decimal marketValue = 0m;
            decimal costBasis = 0m;
            decimal realized = 0m;

            var positionMap = new Dictionary<string, (decimal qty, decimal cost)>();
            foreach (var t in trades.Where(t => DateOnly.FromDateTime(t.ExecutedAt) <= date))
            {
                if (!positionMap.ContainsKey(t.AssetSymbol))
                    positionMap[t.AssetSymbol] = (0m, 0m);

                var (qty, cost) = positionMap[t.AssetSymbol];
                if (t.Side == TradeSide.Buy)
                {
                    positionMap[t.AssetSymbol] = (qty + t.Quantity, cost + (t.Quantity * t.Price + t.Fee));
                }
                else
                {
                    var avg = qty > 0 ? cost / qty : 0m;
                    realized += (t.Price - avg) * t.Quantity - t.Fee;
                    positionMap[t.AssetSymbol] = (qty - t.Quantity, cost - avg * t.Quantity);
                }
            }

            foreach (var (sym, (qty, cost)) in positionMap)
            {
                if (qty <= 0) continue;
                if (!dayPrices.TryGetValue(sym, out var price)) continue;
                marketValue += qty * price;
                costBasis += cost;
            }

            var unrealized = marketValue - costBasis;
            var snap = await _db.PortfolioValueSnapshots
                .FirstOrDefaultAsync(s => s.SubPortfolioId == portfolioId && s.Date == date, ct);

            if (snap is null)
            {
                _db.PortfolioValueSnapshots.Add(new PortfolioValueSnapshot
                {
                    SubPortfolioId = portfolioId,
                    Date = date,
                    MarketValue = new Money(marketValue, CurrencyCode.USD),
                    CostBasis = new Money(costBasis, CurrencyCode.USD),
                    RealizedPnL = new Money(realized, CurrencyCode.USD),
                    UnrealizedPnL = new Money(unrealized, CurrencyCode.USD)
                });
                snapshotsCreated++;
            }
            else
            {
                snap.MarketValue = new Money(marketValue, CurrencyCode.USD);
                snap.CostBasis = new Money(costBasis, CurrencyCode.USD);
                snap.RealizedPnL = new Money(realized, CurrencyCode.USD);
                snap.UnrealizedPnL = new Money(unrealized, CurrencyCode.USD);
            }
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            snapshotsCreated,
            pricesCreated,
            fromDate = startDate,
            toDate = today,
            symbols,
            note = "Synthetic data — not actual historical prices."
        });
    }

    [HttpDelete("api/dev/clear-history/{portfolioId:guid}")]
    public async Task<IActionResult> ClearHistory(Guid portfolioId, CancellationToken ct = default)
    {
        var snaps = await _db.PortfolioValueSnapshots
            .Where(s => s.SubPortfolioId == portfolioId)
            .ToListAsync(ct);
        _db.PortfolioValueSnapshots.RemoveRange(snaps);

        var seedPrices = await _db.PriceSnapshots
            .Where(p => p.Source == "dev-seed")
            .ToListAsync(ct);
        _db.PriceSnapshots.RemoveRange(seedPrices);

        await _db.SaveChangesAsync(ct);
        return Ok(new { snapshotsRemoved = snaps.Count, pricesRemoved = seedPrices.Count });
    }
}
