using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Services;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/kpis")]
public class KpisController : ControllerBase
{
    private readonly KpiService _service;

    public KpisController(KpiService service) => _service = service;

    [HttpGet]
    public Task<KpiSummaryDto> Get(Guid portfolioId, CancellationToken ct) =>
        _service.GetAsync(portfolioId, ct);
}
