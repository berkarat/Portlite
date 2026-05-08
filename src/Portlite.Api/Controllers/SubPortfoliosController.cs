using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Mappings;
using Portlite.Api.Services;
using Portlite.Domain.Enums;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
[Route("api/portfolios")]
public class SubPortfoliosController : ControllerBase
{
    private readonly SubPortfolioService _service;
    private readonly PortfolioSnapshotService _snapshotService;

    public SubPortfoliosController(SubPortfolioService service, PortfolioSnapshotService snapshotService)
    {
        _service = service;
        _snapshotService = snapshotService;
    }

    [HttpGet]
    public Task<List<SubPortfolioDto>> List(CancellationToken ct) => _service.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<SubPortfolioDto> Get(Guid id, CancellationToken ct) => _service.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<SubPortfolioDto>> Create(CreateSubPortfolioRequest req, CancellationToken ct)
    {
        var dto = await _service.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public Task<SubPortfolioDto> Update(Guid id, UpdateSubPortfolioRequest req, CancellationToken ct) =>
        _service.UpdateAsync(id, req, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        await _service.DeleteAsync(id, force, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/cash-balance")]
    public async Task<MoneyDto> GetCashBalance(Guid id, CancellationToken ct)
    {
        // Verify portfolio exists (throws NotFoundException via service if not)
        await _service.GetAsync(id, ct);
        var balance = await _snapshotService.CalculateCashBalanceAsync(id, CurrencyCode.USD, ct);
        return balance.ToDto();
    }
}
