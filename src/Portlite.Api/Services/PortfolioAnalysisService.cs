using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portlite.Domain.Common;
using Portlite.Domain.Entities;
using Portlite.Domain.Enums;
using Portlite.Infrastructure.Ai;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class PortfolioAnalysisService
{
    private readonly PortliteDbContext _db;
    private readonly PositionCalculator _positions;
    private readonly PortfolioSnapshotService _snapshots;
    private readonly IAiAnalysisClient _ai;
    private readonly ILogger<PortfolioAnalysisService> _log;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public PortfolioAnalysisService(
        PortliteDbContext db,
        PositionCalculator positions,
        PortfolioSnapshotService snapshots,
        IAiAnalysisClient ai,
        ILogger<PortfolioAnalysisService> log)
    {
        _db = db;
        _positions = positions;
        _snapshots = snapshots;
        _ai = ai;
        _log = log;
    }

    public async Task<PortfolioAnalysisDto> AnalyzeAsync(
        Guid subPortfolioId, bool forceRefresh, CancellationToken ct = default)
    {
        var portfolio = await _db.SubPortfolios.FindAsync([subPortfolioId], ct)
            ?? throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        var positions = (await _positions.CalculateForPortfolioAsync(subPortfolioId, ct))
            .Where(p => p.Quantity > 0)
            .ToList();
        if (positions.Count == 0)
            throw new BadRequestException("Analiz için en az 1 açık pozisyon gerekli.");

        var cash = await _snapshots.CalculateCashBalanceAsync(subPortfolioId, CurrencyCode.USD, ct);
        var snapshots = await _db.PortfolioValueSnapshots
            .Where(s => s.SubPortfolioId == subPortfolioId)
            .OrderByDescending(s => s.Date).Take(30)
            .ToListAsync(ct);

        var hash = ComputeContentHash(positions, cash, snapshots.FirstOrDefault()?.Date);

        if (!forceRefresh)
        {
            var cached = await _db.PortfolioAnalyses
                .Where(a => a.SubPortfolioId == subPortfolioId && a.ContentHash == hash)
                .OrderByDescending(a => a.GeneratedAt)
                .FirstOrDefaultAsync(ct);
            if (cached is not null && DateTime.UtcNow - cached.GeneratedAt < CacheTtl)
            {
                _log.LogInformation("Analysis cache hit for {Portfolio}", subPortfolioId);
                return Hydrate(cached, fromCache: true);
            }
        }

        var (system, user) = BuildPrompts(portfolio.Name, positions, cash, snapshots);
        var ai = await _ai.CompleteJsonAsync(system, user, ct);

        AnalysisResultRaw parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AnalysisResultRaw>(ai.Content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("AI yanıtı boş geldi.");
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "AI JSON parse failed. Raw: {Raw}", ai.Content);
            throw new InvalidOperationException("AI yanıtı JSON olarak parse edilemedi.", ex);
        }

        ValidateResult(parsed);

        var entity = new PortfolioAnalysis
        {
            SubPortfolioId = subPortfolioId,
            GeneratedAt = DateTime.UtcNow,
            ContentHash = hash,
            ResultJson = JsonSerializer.Serialize(parsed),
            InputTokens = ai.InputTokens,
            OutputTokens = ai.OutputTokens
        };
        _db.PortfolioAnalyses.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Hydrate(entity, fromCache: false);
    }

    public async Task<List<PortfolioAnalysisDto>> GetHistoryAsync(
        Guid subPortfolioId, int take, CancellationToken ct = default)
    {
        var rows = await _db.PortfolioAnalyses
            .Where(a => a.SubPortfolioId == subPortfolioId)
            .OrderByDescending(a => a.GeneratedAt).Take(take)
            .ToListAsync(ct);
        return rows.Select(r => Hydrate(r, fromCache: true)).ToList();
    }

    private (string system, string user) BuildPrompts(
        string portfolioName,
        List<PositionDto> positions,
        Money cash,
        List<PortfolioValueSnapshot> snapshots)
    {
        var system = """
Sen deneyimli bir portföy analistisin. Kullanıcının ABD hisse portföy state'ini al, kısa ve aksiyon-alınabilir bir analiz yap. Yanıtın MUTLAKA aşağıdaki JSON şemasında olsun (başka metin yok):

{
  "summary": "2-3 cümle Türkçe özet",
  "warnings": [{ "severity": "high|med|low", "title": "...", "detail": "..." }],
  "suggestions": [{ "priority": "high|med|low", "action": "...", "reasoning": "..." }],
  "marketContext": "Genel piyasa yorumu, 1-2 cümle"
}

Maksimum 5 uyarı, 5 öneri. Türkçe yaz. Spesifik ol — "diversifikasyona dikkat et" değil, "NVDA portföyünün %78'i, %30'a indirilmesi düşünülebilir" gibi sayılarla konuş. severity ve priority alanları sadece "high", "med", veya "low" olabilir.
""";

        var totalMv = positions.Sum(p => p.MarketValue ?? 0m);
        var totalEquity = totalMv + cash.Amount;

        var posData = positions.Select(p => new
        {
            symbol = p.AssetSymbol,
            name = p.AssetName,
            quantity = p.Quantity,
            avgCost = p.AverageCost,
            currentPrice = p.CurrentPrice,
            marketValue = p.MarketValue,
            unrealizedPnL = p.UnrealizedPnL,
            unrealizedPct = p.TotalCost > 0 && p.UnrealizedPnL.HasValue
                ? Math.Round(p.UnrealizedPnL.Value / p.TotalCost * 100m, 2)
                : (decimal?)null,
            weightPct = totalEquity > 0 && p.MarketValue.HasValue
                ? Math.Round(p.MarketValue.Value / totalEquity * 100m, 2)
                : (decimal?)null,
            dayChangePct = p.DayChangePercent
        });

        var snap30 = snapshots.OrderBy(s => s.Date).LastOrDefault();
        var snap0 = snapshots.OrderBy(s => s.Date).FirstOrDefault();
        var perf30Pct = (snap0 is not null && snap30 is not null && snap0.MarketValue.Amount > 0)
            ? Math.Round((snap30.MarketValue.Amount + cash.Amount - (snap0.MarketValue.Amount + cash.Amount))
                / (snap0.MarketValue.Amount + cash.Amount) * 100m, 2)
            : (decimal?)null;

        var payload = new
        {
            portfolioName,
            currency = "USD",
            totalEquity,
            totalMarketValue = totalMv,
            cashBalance = cash.Amount,
            positionCount = positions.Count,
            positions = posData,
            last30DayReturnPct = perf30Pct,
            snapshotDate = snap30?.Date.ToString("yyyy-MM-dd")
        };

        var user = "Portföy state'i:\n" + JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return (system, user);
    }

    private string ComputeContentHash(
        List<PositionDto> positions, Money cash, DateOnly? snapshotDate)
    {
        var sb = new StringBuilder();
        sb.Append(snapshotDate?.ToString("yyyy-MM-dd") ?? "no-snap").Append('|');
        sb.Append(Math.Round(cash.Amount / 100m) * 100m).Append('|');
        foreach (var p in positions.OrderBy(x => x.AssetSymbol))
            sb.Append(p.AssetSymbol).Append(':').Append(p.Quantity).Append(',');
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void ValidateResult(AnalysisResultRaw r)
    {
        if (r is null) throw new InvalidOperationException("Boş analiz sonucu");
        var validSev = new[] { "high", "med", "low" };
        foreach (var w in r.Warnings ?? new())
            if (!validSev.Contains(w.Severity?.ToLowerInvariant()))
                throw new InvalidOperationException($"Geçersiz severity: {w.Severity}");
        foreach (var s in r.Suggestions ?? new())
            if (!validSev.Contains(s.Priority?.ToLowerInvariant()))
                throw new InvalidOperationException($"Geçersiz priority: {s.Priority}");
    }

    private PortfolioAnalysisDto Hydrate(PortfolioAnalysis a, bool fromCache)
    {
        var raw = JsonSerializer.Deserialize<AnalysisResultRaw>(a.ResultJson)!;
        return new PortfolioAnalysisDto(
            a.Id, a.SubPortfolioId, a.GeneratedAt,
            raw.Summary, raw.Warnings ?? new(), raw.Suggestions ?? new(),
            raw.MarketContext, fromCache);
    }
}
