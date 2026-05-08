using Microsoft.AspNetCore.Mvc;
using Portlite.Api.Services;
using Portlite.Domain.Enums;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Portlite.Api.Controllers;

[ApiController]
[Route("api/assets/{symbol}/quote")]
public class QuotesController : ControllerBase
{
    private readonly IPriceProvider _provider;
    private readonly PriceSnapshotStore _store;
    private readonly PortliteDbContext _db;

    public QuotesController(IPriceProvider provider, PriceSnapshotStore store, PortliteDbContext db)
    {
        _provider = provider;
        _store = store;
        _db = db;
    }

    [HttpGet]
    public async Task<QuoteDto> Get(string symbol, CancellationToken ct)
    {
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Symbol == symbol, ct)
            ?? throw new NotFoundException($"Asset '{symbol}' not registered. Create it first.");

        var quote = await _provider.GetQuoteAsync(symbol, ct);
        await _store.SaveAsync(quote, ct);

        var change = quote.Current - quote.PreviousClose;
        var pct = quote.PreviousClose != 0 ? change / quote.PreviousClose * 100m : 0m;

        return new QuoteDto(
            quote.Symbol,
            quote.Current,
            quote.PreviousClose,
            change,
            pct,
            quote.DayHigh,
            quote.DayLow,
            quote.DayOpen,
            quote.Timestamp,
            asset.Currency,
            quote.Source);
    }
}
