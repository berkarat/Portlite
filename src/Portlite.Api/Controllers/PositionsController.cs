using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Services;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/positions")]
public class PositionsController : ControllerBase
{
    private readonly PositionCalculator _calculator;

    public PositionsController(PositionCalculator calculator) => _calculator = calculator;

    [HttpGet]
    public Task<List<PositionDto>> List(Guid portfolioId, CancellationToken ct) =>
        _calculator.CalculateForPortfolioAsync(portfolioId, ct);
}
