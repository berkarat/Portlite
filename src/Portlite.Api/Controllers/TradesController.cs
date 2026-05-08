using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Services;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
public class TradesController : ControllerBase
{
    private readonly TradeService _service;

    public TradesController(TradeService service) => _service = service;

    [HttpGet("api/portfolios/{portfolioId:guid}/trades")]
    public Task<List<TradeDto>> List(Guid portfolioId, CancellationToken ct) =>
        _service.ListByPortfolioAsync(portfolioId, ct);

    [HttpPost("api/portfolios/{portfolioId:guid}/trades")]
    public async Task<ActionResult<TradeDto>> Create(Guid portfolioId, CreateTradeRequest req, CancellationToken ct)
    {
        var dto = await _service.CreateAsync(portfolioId, req, ct);
        return Created($"/api/trades/{dto.Id}", dto);
    }

    [HttpDelete("api/trades/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
