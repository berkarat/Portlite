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
}
