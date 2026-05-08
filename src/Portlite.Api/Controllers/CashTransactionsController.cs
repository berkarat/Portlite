using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Services;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
public class CashTransactionsController : ControllerBase
{
    private readonly CashTransactionService _service;

    public CashTransactionsController(CashTransactionService service) => _service = service;

    [HttpGet("api/portfolios/{portfolioId:guid}/cash")]
    public Task<List<CashTransactionDto>> List(Guid portfolioId, CancellationToken ct) =>
        _service.ListByPortfolioAsync(portfolioId, ct);

    [HttpPost("api/portfolios/{portfolioId:guid}/cash")]
    public async Task<ActionResult<CashTransactionDto>> Create(Guid portfolioId, CreateCashTransactionRequest req, CancellationToken ct)
    {
        var dto = await _service.CreateAsync(portfolioId, req, ct);
        return Created($"/api/cash/{dto.Id}", dto);
    }

    [HttpDelete("api/cash/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
