using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Services;
using Portlite.Domain.Enums;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
[Route("api/assets")]
public class AssetsController : ControllerBase
{
    private readonly AssetService _service;

    public AssetsController(AssetService service) => _service = service;

    [HttpGet]
    public Task<List<AssetDto>> List([FromQuery] AssetType? type, CancellationToken ct) =>
        _service.ListAsync(type, ct);

    [HttpGet("search")]
    public async Task<List<SymbolSearchHitDto>> Search([FromQuery] string q, CancellationToken ct)
    {
        var hits = await _service.SearchSymbolsAsync(q, ct);
        return hits.Select(h => new SymbolSearchHitDto(h.Symbol, h.DisplaySymbol, h.Description, h.Type)).ToList();
    }

    [HttpPost("lookup")]
    public Task<AssetDto> Lookup([FromBody] SymbolLookupRequest req, CancellationToken ct) =>
        _service.UpsertFromSymbolAsync(req.Symbol, ct);

    [HttpGet("{symbol}")]
    public Task<AssetDto> Get(string symbol, CancellationToken ct) =>
        _service.GetBySymbolAsync(symbol, ct);

    [HttpPost]
    public async Task<ActionResult<AssetDto>> Create(CreateAssetRequest req, CancellationToken ct)
    {
        var dto = await _service.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { symbol = dto.Symbol }, dto);
    }

    [HttpPut("{symbol}/theme")]
    public Task<AssetDto> UpdateTheme(string symbol, [FromBody] UpdateAssetThemeRequest req, CancellationToken ct) =>
        _service.UpdateThemeAsync(symbol, req.Theme, ct);

    [HttpPost("themes/auto")]
    public async Task<AutoThemeResult> AutoAssignThemes([FromBody] AutoThemeRequest? req, CancellationToken ct)
    {
        var changed = await _service.AutoAssignThemesAsync(req?.Symbols, req?.Overwrite ?? false, ct);
        return new AutoThemeResult(changed);
    }

    [HttpGet("{symbol}/history")]
    public async Task<List<PricePointDto>> History(
        string symbol,
        [FromServices] Portlite.Infrastructure.Persistence.PortliteDbContext db,
        CancellationToken ct)
    {
        var sym = symbol.ToUpperInvariant();
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.PriceSnapshots
                .Where(p => p.AssetSymbol == sym)
                .OrderBy(p => p.Date)
                .Select(p => new PricePointDto(p.Date, p.Close)),
            ct);
        return rows;
    }

    // Birden çok sembol için son birkaç günün kapanış serisi (mini sparkline grafikleri).
    // Gerçek geçmiş fiyatları FMP/Yahoo sağlayıcısından çeker.
    [HttpGet("sparklines")]
    public async Task<List<SparklineDto>> Sparklines(
        [FromQuery] string symbols,
        [FromServices] Portlite.Infrastructure.MarketData.IHistoricalPriceProvider history,
        [FromQuery] int days = 14,
        CancellationToken ct = default)
    {
        var syms = (symbols ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .Take(50)
            .ToList();

        var window = Math.Clamp(days, 2, 60);

        // En fazla 5 eşzamanlı dış istek (hız + rate-limit dengesi).
        using var gate = new SemaphoreSlim(5);
        var tasks = syms.Select(async sym =>
        {
            await gate.WaitAsync(ct);
            try
            {
                // 40 bar iste: sağlayıcının iç eşiklerini (>=15) aşıp kısa pencereyi güvenle keselim.
                var bars = await history.GetDailyBarsAsync(sym, 40, ct);
                var closes = bars
                    .OrderBy(b => b.Date)
                    .TakeLast(window)
                    .Select(b => b.Close)
                    .ToList();
                return new SparklineDto(sym, closes);
            }
            catch
            {
                return new SparklineDto(sym, new List<decimal>());
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }
}
