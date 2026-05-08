using Microsoft.AspNetCore.Mvc;
using Portlite.Api.BackgroundJobs;
using Portlite.Api.Services;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
public class SnapshotsController : ControllerBase
{
    private readonly PortfolioSnapshotService _service;
    private readonly DailySnapshotHostedService _job;

    public SnapshotsController(PortfolioSnapshotService service, DailySnapshotHostedService job)
    {
        _service = service;
        _job = job;
    }

    [HttpPost("api/portfolios/{portfolioId:guid}/snapshot")]
    public Task<PortfolioSnapshotDto> Create(Guid portfolioId, CancellationToken ct) =>
        _service.CreateOrUpdateSnapshotAsync(portfolioId, ct);

    [HttpGet("api/portfolios/{portfolioId:guid}/snapshots")]
    public Task<List<PortfolioSnapshotDto>> List(
        Guid portfolioId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct) =>
        _service.GetHistoryAsync(portfolioId, from, to, ct);

    [HttpPost("api/snapshots/run-now")]
    public async Task<IActionResult> RunNow(CancellationToken ct)
    {
        var count = await _job.RunOnceAsync(ct);
        return Ok(new { snapshotsCreated = count, runAt = DateTime.UtcNow });
    }
}
