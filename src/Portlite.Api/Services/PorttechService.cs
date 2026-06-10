using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portlite.Domain.Entities;
using Portlite.Infrastructure.Ai;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class PorttechService
{
    private readonly PortliteDbContext _db;
    private readonly PositionCalculator _positions;
    private readonly IHistoricalPriceProvider _history;
    private readonly IAiAnalysisClient _ai;
    private readonly ILogger<PorttechService> _log;

    public PorttechService(
        PortliteDbContext db,
        PositionCalculator positions,
        IHistoricalPriceProvider history,
        IAiAnalysisClient ai,
        ILogger<PorttechService> log)
    {
        _db = db;
        _positions = positions;
        _history = history;
        _ai = ai;
        _log = log;
    }

    public async Task<PorttechReportDto> GenerateAsync(Guid subPortfolioId, CancellationToken ct = default)
    {
        var portfolio = await _db.SubPortfolios.FindAsync([subPortfolioId], ct)
            ?? throw new NotFoundException($"SubPortfolio {subPortfolioId} not found.");

        var positions = (await _positions.CalculateForPortfolioAsync(subPortfolioId, ct))
            .Where(p => p.Quantity > 0)
            .ToList();

        if (positions.Count == 0)
            throw new BadRequestException("Porttech için en az 1 açık pozisyon gerekli.");

        // Fetch technical data for each position
        var technicals = new List<TechnicalIndicators>();
        foreach (var pos in positions)
        {
            try
            {
                var bars = await _history.GetDailyBarsAsync(pos.AssetSymbol, 252, ct);
                if (bars.Count < 15)
                {
                    _log.LogWarning("Insufficient data for {Symbol}: {Count} bars", pos.AssetSymbol, bars.Count);
                    continue;
                }
                var ti = TechnicalCalculator.Calculate(bars, pos.AverageCost);
                technicals.Add(ti);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to get technical data for {Symbol}", pos.AssetSymbol);
            }
        }

        // Build AI prompt
        var (system, user) = BuildPrompts(portfolio.Name, positions, technicals);
        var aiResult = await _ai.CompleteJsonAsync(system, user, ct);

        // Save to DB
        var report = new PorttechReport
        {
            SubPortfolioId = subPortfolioId,
            ReportDate = DateOnly.FromDateTime(DateTime.UtcNow),
            TechnicalDataJson = JsonSerializer.Serialize(technicals),
            ReportJson = aiResult.Content,
            InputTokens = aiResult.InputTokens,
            OutputTokens = aiResult.OutputTokens
        };
        _db.PorttechReports.Add(report);
        await _db.SaveChangesAsync(ct);

        return ToDto(report);
    }

    public async Task<PorttechReportDto?> GetLatestAsync(Guid subPortfolioId, CancellationToken ct = default)
    {
        var report = await _db.PorttechReports
            .Where(r => r.SubPortfolioId == subPortfolioId)
            .OrderByDescending(r => r.ReportDate)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return report is null ? null : ToDto(report);
    }

    public async Task<List<PorttechReportDto>> GetHistoryAsync(Guid subPortfolioId, int take = 10, CancellationToken ct = default)
    {
        var reports = await _db.PorttechReports
            .Where(r => r.SubPortfolioId == subPortfolioId)
            .OrderByDescending(r => r.ReportDate)
            .ThenByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        return reports.Select(r => ToDto(r)).ToList();
    }

    private (string system, string user) BuildPrompts(
        string portfolioName,
        List<PositionDto> positions,
        List<TechnicalIndicators> technicals)
    {
        var totalMv = positions.Sum(p => p.MarketValue ?? 0m);

        var system = """
Sen deneyimli bir portföy teknik analistisin. Kullanıcının ABD hisse portföyü için PORTTECH raporu üreteceksin.
Her pozisyon için teknik veriler (RSI, 50MA, 200MA, 52-hafta high, TC skor) ve pozisyon bilgileri (maliyet, lot, günlük %) sağlanacak.

Yanıtın MUTLAKA aşağıdaki JSON şemasında olsun (başka metin ekleme):

{
  "executive_summary": "3-5 cümle Türkçe genel durum özeti",
  "exec_grid": {
    "rsi_overbought_count": 0,
    "near_52h_count": 0,
    "below_200ma_count": 0,
    "tc_below_40_count": 0,
    "tc_above_85_count": 0,
    "rsi_overbought_symbols": [],
    "near_52h_symbols": [],
    "below_200ma_symbols": [],
    "tc_below_40_symbols": [],
    "tc_above_85_symbols": []
  },
  "alerts": [
    {
      "severity": "kritik|uyari|bilgi|firsat",
      "symbol": "XXX",
      "message": "Açıklama..."
    }
  ],
  "actions": [
    {
      "symbol": "XXX",
      "action": "STOP SIKILAŞTIR|HOLD / CONFLUENCE|İZLE / EK ALIM|İZLE / DÖNÜŞ İZLE|AZALT",
      "reasoning": "VERİ → OLAY → ETKİ formatında açıklama"
    }
  ],
  "position_summary": [
    {
      "symbol": "XXX",
      "tc_score": 80,
      "tc_label": "Sağlıklı",
      "rsi": 55.2,
      "sma50": 150.0,
      "sma200": 120.0,
      "high_52w": 180.0,
      "current_price": 165.0,
      "avg_cost": 140.0,
      "day_change_pct": -2.5,
      "pnl_pct": 17.8
    }
  ]
}

Kurallar:
- alerts: max 10, en kritikten başla
- actions: TÜM pozisyonlar için aksiyon öner
- Spesifik ol: "$148 hard stop", "100 lot trim $230 üstünde" gibi fiyat/lot belirt
- Türkçe yaz
- severity: "kritik" = acil aksiyon, "uyari" = izle, "bilgi" = bilgilendirme, "firsat" = alım fırsatı
""";

        var positionData = positions.Select(p =>
        {
            var tech = technicals.FirstOrDefault(t => t.Symbol == p.AssetSymbol);
            return new
            {
                symbol = p.AssetSymbol,
                name = p.AssetName,
                quantity = p.Quantity,
                avgCost = p.AverageCost,
                currentPrice = p.CurrentPrice,
                marketValue = p.MarketValue,
                dayChangePct = p.DayChangePercent,
                unrealizedPnlPct = p.TotalCost > 0 && p.UnrealizedPnL.HasValue
                    ? Math.Round(p.UnrealizedPnL.Value / p.TotalCost * 100m, 2)
                    : (decimal?)null,
                weightPct = totalMv > 0 && p.MarketValue.HasValue
                    ? Math.Round(p.MarketValue.Value / totalMv * 100m, 2)
                    : (decimal?)null,
                rsi = tech?.Rsi14,
                sma50 = tech?.Sma50,
                sma200 = tech?.Sma200,
                high52w = tech?.High52Week,
                low52w = tech?.Low52Week,
                tcScore = tech?.TcScore,
                tcLabel = tech?.TcLabel,
                distFrom52High = tech?.DistFrom52High,
                distFrom200Ma = tech?.DistFrom200Ma
            };
        });

        var payload = new
        {
            portfolioName,
            reportDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            totalMarketValue = totalMv,
            positionCount = positions.Count,
            positions = positionData
        };

        var user = "Portföy teknik verileri:\n" + JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return (system, user);
    }

    private static PorttechReportDto ToDto(PorttechReport report)
    {
        return new PorttechReportDto
        {
            Id = report.Id,
            ReportDate = report.ReportDate,
            CreatedAt = report.CreatedAt,
            TechnicalDataJson = report.TechnicalDataJson,
            ReportJson = report.ReportJson,
            InputTokens = report.InputTokens,
            OutputTokens = report.OutputTokens
        };
    }
}
