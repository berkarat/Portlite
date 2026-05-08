using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Services;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Controllers;

[ApiController]
public class NewsController : ControllerBase
{
    private readonly NewsService _service;

    public NewsController(NewsService service) => _service = service;

    [HttpGet("api/news/portfolio/{portfolioId:guid}")]
    public Task<List<NewsItemDto>> ForPortfolio(
        Guid portfolioId,
        [FromQuery] int days = 7,
        [FromQuery] int take = 30,
        CancellationToken ct = default)
        => _service.GetForPortfolioAsync(portfolioId, days, take, ct);

    [HttpGet("api/news/{symbol}")]
    public Task<List<NewsItemDto>> ForSymbol(
        string symbol,
        [FromQuery] int days = 14,
        [FromQuery] int take = 30,
        CancellationToken ct = default)
        => _service.GetForSymbolAsync(symbol, days, take, ct);
}
