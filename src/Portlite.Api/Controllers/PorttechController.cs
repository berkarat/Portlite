using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Services;

namespace Portlite.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PorttechController : ControllerBase
{
    private readonly PorttechService _porttech;

    public PorttechController(PorttechService porttech) => _porttech = porttech;

    [HttpPost("generate/{subPortfolioId:guid}")]
    public async Task<IActionResult> Generate(Guid subPortfolioId, CancellationToken ct)
    {
        var report = await _porttech.GenerateAsync(subPortfolioId, ct);
        return Ok(report);
    }

    [HttpGet("latest/{subPortfolioId:guid}")]
    public async Task<IActionResult> GetLatest(Guid subPortfolioId, CancellationToken ct)
    {
        var report = await _porttech.GetLatestAsync(subPortfolioId, ct);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpGet("history/{subPortfolioId:guid}")]
    public async Task<IActionResult> GetHistory(Guid subPortfolioId, [FromQuery] int take = 10, CancellationToken ct = default)
    {
        var reports = await _porttech.GetHistoryAsync(subPortfolioId, take, ct);
        return Ok(reports);
    }
}
