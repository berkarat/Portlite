using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Services;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
[Route("api/watchlist")]
public class WatchlistController : ControllerBase
{
    private readonly WatchlistService _service;

    public WatchlistController(WatchlistService service) => _service = service;

    [HttpGet]
    public Task<List<WatchlistItemDto>> List(CancellationToken ct) => _service.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<WatchlistItemDto>> Add(AddWatchlistRequest req, CancellationToken ct)
    {
        var dto = await _service.AddAsync(req, ct);
        return Created($"/api/watchlist/{dto.Id}", dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await _service.RemoveAsync(id, ct);
        return NoContent();
    }

    [HttpPost("refresh")]
    public Task<List<WatchlistItemDto>> Refresh(CancellationToken ct) => _service.RefreshAllPricesAsync(ct);
}
