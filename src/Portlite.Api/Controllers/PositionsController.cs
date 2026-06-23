using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portlite.Api.Services;
using Portlite.Domain.Entities;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/positions")]
public class PositionsController : ControllerBase
{
    private readonly PositionCalculator _calculator;

    public PositionsController(PositionCalculator calculator) => _calculator = calculator;

    [HttpGet]
    public Task<List<PositionDto>> List(Guid portfolioId, CancellationToken ct) =>
        _calculator.CalculateForPortfolioAsync(portfolioId, ct);

    // Ortalama maliyeti elle ez (geçmiş işlemler korunur).
    [HttpPut("{symbol}/cost-basis")]
    public async Task<IActionResult> SetCostBasis(
        Guid portfolioId,
        string symbol,
        [FromBody] SetCostBasisRequest req,
        [FromServices] PortliteDbContext db,
        CancellationToken ct)
    {
        var sym = symbol.ToUpperInvariant();
        if (req.AverageCost <= 0)
            return BadRequest("Ortalama maliyet 0'dan büyük olmalı.");

        var existing = await db.PositionCostOverrides
            .FirstOrDefaultAsync(o => o.SubPortfolioId == portfolioId && o.AssetSymbol == sym, ct);

        if (existing is null)
        {
            db.PositionCostOverrides.Add(new PositionCostOverride
            {
                SubPortfolioId = portfolioId,
                AssetSymbol = sym,
                AverageCost = req.AverageCost
            });
        }
        else
        {
            existing.AverageCost = req.AverageCost;
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Override'ı kaldır (hesaplanan ortalamaya geri dön).
    [HttpDelete("{symbol}/cost-basis")]
    public async Task<IActionResult> ClearCostBasis(
        Guid portfolioId,
        string symbol,
        [FromServices] PortliteDbContext db,
        CancellationToken ct)
    {
        var sym = symbol.ToUpperInvariant();
        var existing = await db.PositionCostOverrides
            .FirstOrDefaultAsync(o => o.SubPortfolioId == portfolioId && o.AssetSymbol == sym, ct);
        if (existing is not null)
        {
            db.PositionCostOverrides.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }
}
