using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portlite.Domain.Common;
using Portlite.Domain.Entities;
using Portlite.Domain.Enums;
using Portlite.Infrastructure.Ai;
using Portlite.Infrastructure.MarketData;
using Portlite.Infrastructure.Persistence;
using Portlite.Shared.Dtos;

namespace Portlite.Api.Services;

public class PorttechService
{
    private readonly PortliteDbContext _db;
    private readonly PositionCalculator _positions;
    private readonly PortfolioSnapshotService _snapshots;
    private readonly IHistoricalPriceProvider _history;
    private readonly IAiAnalysisClient _ai;
    private readonly ILogger<PorttechService> _log;

    public PorttechService(
        PortliteDbContext db,
        PositionCalculator positions,
        PortfolioSnapshotService snapshots,
        IHistoricalPriceProvider history,
        IAiAnalysisClient ai,
        ILogger<PorttechService> log)
    {
        _db = db;
        _positions = positions;
        _snapshots = snapshots;
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
        var recentTrades = await _db.Trades
            .Where(t => t.SubPortfolioId == subPortfolioId)
            .OrderByDescending(t => t.ExecutedAt)
            .Take(10)
            .ToListAsync(ct);

        var cashBalance = await _snapshots.CalculateCashBalanceAsync(subPortfolioId, CurrencyCode.USD, ct);

        var (system, user) = BuildPrompts(portfolio.Name, positions, technicals, recentTrades, cashBalance);
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
        List<TechnicalIndicators> technicals,
        List<Trade> recentTrades,
        Money cashBalance)
    {
        var totalMv = positions.Sum(p => p.MarketValue ?? 0m);
        var totalEquity = totalMv + cashBalance.Amount;
        var cashPct = totalEquity > 0 ? Math.Round(cashBalance.Amount / totalEquity * 100m, 1) : 0m;

        var system = """
Sen deneyimli bir portföy teknik analistisin. Kullanıcının ABD hisse portföyü için günlük PORTTECH teknik tarama raporu üreteceksin.

GÖREV: Her pozisyon için teknik veriler (RSI14, EMA21, EMA50, SMA200, 52H), pozisyon bilgileri (maliyet, lot, K/Z), nakit durumu ve son işlemler sağlanacak. Sen bunları birleştirip AKSİYON ÇEKİLEBİLİR bir rapor üreteceksin.

ÇIKTI FORMATI: Yanıtın MUTLAKA aşağıdaki JSON şemasında olsun. Başka metin, markdown, açıklama EKLEMEYİN — sadece JSON:

{
  "executive_summary": "5-8 cümle Türkçe genel durum özeti. İlk cümlede portföyün bugünkü durumu. Ardından en acil 3 madde. Son cümle: önerilen genel tavır (savunmacı, agresif, nötr). Sayılarla konuş.",
  "context_box": {
    "total_equity": "$X",
    "position_value": "$X",
    "cash_balance": "$X",
    "cash_pct": "X%",
    "position_count": 0,
    "cash_stance": "savunmacı|nötr|agresif"
  },
  "exec_grid": {
    "rsi_overbought_count": 0,
    "near_52h_count": 0,
    "below_ema50_count": 0,
    "below_cost_count": 0,
    "tc_below_40_count": 0,
    "tc_above_85_count": 0,
    "rsi_overbought_symbols": [],
    "near_52h_symbols": [],
    "below_ema50_symbols": [],
    "below_cost_symbols": [],
    "tc_below_40_symbols": [],
    "tc_above_85_symbols": []
  },
  "alerts": [
    {
      "category": "EMA50 KAYBI|SERT DÜŞÜŞ|MOMENTUM KAYBI|BÜYÜK POZ ZARAR|AŞIRI ALIM|VERİ EKSİĞİ|GÜÇLÜ TUTAN",
      "severity": "kritik|uyari|bilgi|firsat",
      "symbol": "XXX",
      "message": "Detaylı açıklama — fiyat, EMA50 seviyesi, % fark, ne yapılmalı."
    }
  ],
  "actions": [
    {
      "symbol": "XXX",
      "action": "STOP SIKILAŞTIR|HOLD / CONFLUENCE|İZLE / EK ALIM|İZLE|AZALT|BINARY TAKİP|DÖNÜŞ İZLE",
      "reasoning": "K/Z durumu, teknik pozisyon, neden bu aksiyon.",
      "stop_level": "$XXX (neden burası)",
      "target_or_reclaim": "$XXX kapanış güven verir / ek alım noktası",
      "yani": "Günlük dilde 1 cümle: kullanıcının bugün ne yapması gerektiğini söyle. Örnek: '$148 altında kapanırsa yarın sat, üstünde tutarsa bekle.'"
    }
  ],
  "position_summary": [
    {
      "symbol": "XXX",
      "tc_score": 80,
      "tc_label": "Sağlıklı",
      "rsi": 55.2,
      "ema21": 160.0,
      "ema50": 150.0,
      "ema50_dist_pct": 5.2,
      "sma200": 120.0,
      "high_52w": 180.0,
      "current_price": 165.0,
      "avg_cost": 140.0,
      "day_change_pct": -2.5,
      "pnl_pct": 17.8,
      "unrealized_pnl": 5000.0
    }
  ],
  "summary_note": "2-3 cümle Türkçe. Tablodaki aksiyonları özetler. Kaç hisse stop sıkılaştırma, kaç hisse izle, agresif ekleme var mı? Aksiyon sözlüğü kısa açıkla."
}

KURALLAR:
1. alerts: max 10, en kritikten başla. "GÜÇLÜ TUTAN" kategorisinde kazançta trend sağlam olanları listele.
2. actions: TÜM pozisyonlar için aksiyon öner. Her biri için stop_level ve target_or_reclaim ZORUNLU.
3. stop_level formatı: "$148 (swing low)" veya "$192 (EMA50 -%3)" gibi SEVİYE + GEREKÇE.
4. SPESİFİK OL: "diversifikasyona dikkat" DEĞİL → "$148 hard stop, EMA50 %3 altında kapanırsa çık" gibi.
5. Son işlemleri (recentTrades) DİKKATE AL — dün ne alındıysa bugün o pozisyon için "hedef büyüklüğe ulaştı, ek yok" gibi bağlam ver.
6. Nakit oranını YORUMLA: ≥%15 savunmacı, %5-15 nötr, <%5 agresif.
7. executive_summary'de ilk cümlede bugünkü portföy değişimi ($X, %X), sonra en acil 3 risk/fırsat.
8. Türkçe yaz. Sayılarla konuş.
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
                unrealizedPnl = p.UnrealizedPnL,
                dayChangePct = p.DayChangePercent,
                unrealizedPnlPct = p.TotalCost > 0 && p.UnrealizedPnL.HasValue
                    ? Math.Round(p.UnrealizedPnL.Value / p.TotalCost * 100m, 2)
                    : (decimal?)null,
                weightPct = totalEquity > 0 && p.MarketValue.HasValue
                    ? Math.Round(p.MarketValue.Value / totalEquity * 100m, 2)
                    : (decimal?)null,
                rsi = tech?.Rsi14,
                ema21 = tech?.Ema21,
                ema50 = tech?.Ema50,
                ema50DistPct = tech?.DistFromEma50,
                sma200 = tech?.Sma200,
                high52w = tech?.High52Week,
                low52w = tech?.Low52Week,
                tcScore = tech?.TcScore,
                tcLabel = tech?.TcLabel,
                distFrom52High = tech?.DistFrom52High,
                distFrom200Ma = tech?.DistFrom200Ma
            };
        });

        var tradesData = recentTrades.Select(t => new
        {
            date = t.ExecutedAt.ToString("yyyy-MM-dd"),
            symbol = t.AssetSymbol,
            side = t.Side.ToString(),
            quantity = t.Quantity,
            price = t.Price,
            notes = t.Notes
        });

        var payload = new
        {
            portfolioName,
            reportDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            totalEquity,
            totalMarketValue = totalMv,
            cashBalance = cashBalance.Amount,
            cashPct,
            positionCount = positions.Count,
            positions = positionData,
            recentTrades = tradesData
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
