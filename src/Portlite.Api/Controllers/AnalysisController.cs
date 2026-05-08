using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Services;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
public class AnalysisController : ControllerBase
{
    private readonly PortfolioAnalysisService _service;

    public AnalysisController(PortfolioAnalysisService service) => _service = service;

    [HttpPost("api/portfolios/{portfolioId:guid}/analyze")]
    public Task<PortfolioAnalysisDto> Analyze(
        Guid portfolioId,
        [FromQuery] bool forceRefresh = false,
        CancellationToken ct = default)
        => _service.AnalyzeAsync(portfolioId, forceRefresh, ct);

    [HttpGet("api/portfolios/{portfolioId:guid}/analyses")]
    public Task<List<PortfolioAnalysisDto>> History(
        Guid portfolioId,
        [FromQuery] int take = 10,
        CancellationToken ct = default)
        => _service.GetHistoryAsync(portfolioId, take, ct);
}
