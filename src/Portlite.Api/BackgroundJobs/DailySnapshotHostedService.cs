using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portlite.Api.Services;
using Portlite.Infrastructure.Persistence;

namespace Portlite.Api.BackgroundJobs;

public class DailySnapshotHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<SnapshotJobOptions> _options;
    private readonly ILogger<DailySnapshotHostedService> _log;

    public DailySnapshotHostedService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<SnapshotJobOptions> options,
        ILogger<DailySnapshotHostedService> log)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled)
        {
            _log.LogInformation("Daily snapshot job disabled via configuration.");
            return;
        }

        _log.LogInformation("Daily snapshot job started; will run at {Hour}:00 UTC each day.", opts.DailyRunHourUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = NextRunUtc(_options.CurrentValue.DailyRunHourUtc);
            var delay = nextRun - DateTime.UtcNow;
            _log.LogInformation("Next snapshot run scheduled for {NextRun:O} (in {Hours:F1} h).",
                nextRun, delay.TotalHours);

            try
            {
                await Task.Delay(delay, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Daily snapshot job iteration failed; retrying in 5 minutes.");
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortliteDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<PortfolioSnapshotService>();

        var portfolios = await db.SubPortfolios
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var success = 0;
        foreach (var p in portfolios)
        {
            try
            {
                var snap = await service.CreateOrUpdateSnapshotAsync(p.Id, ct);
                _log.LogInformation(
                    "Snapshot OK for {Code} ({Id}): equity {Equity} {Cur}, missing {Missing}",
                    p.Code, p.Id, snap.TotalEquity.Amount, snap.TotalEquity.Currency, snap.MissingPriceSymbols.Count);
                success++;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Snapshot FAILED for {Code} ({Id})", p.Code, p.Id);
            }
        }

        _log.LogInformation("Daily snapshot run complete: {Success}/{Total} portfolios.", success, portfolios.Count);
        return success;
    }

    private static DateTime NextRunUtc(int hourUtc)
    {
        var now = DateTime.UtcNow;
        var todayTarget = new DateTime(now.Year, now.Month, now.Day, hourUtc, 0, 0, DateTimeKind.Utc);
        return todayTarget > now ? todayTarget : todayTarget.AddDays(1);
    }
}
