using System.Security.Cryptography;
using System.Text;
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

public class PortfolioAnalysisService
{
    private readonly PortliteDbContext _db;
    private readonly PositionCalculator _positions;
    private readonly PortfolioSnapshotService _snapshots;
    private readonly IHistoricalPriceProvider _history;
    private readonly IAiAnalysisClient _ai;
    private readonly ILogger<PortfolioAnalysisService> _log;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public PortfolioAnalysisService(
        PortliteDbContext db,
        PositionCalculator positions,
        PortfolioSnapshotService snapshots,
        IHistoricalPriceProvider history,
        IAiAnalysisClient ai,
        ILogger<PortfolioAnalysisService> log)
    {
        _db = db;
        _positions = positions;
        _snapshots = snapshots;
        _history = history;
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

        // Her açık pozisyon için teknik göstergeleri hesapla (Porttech ile aynı motor).
        var technicals = new List<TechnicalIndicators>();
        foreach (var pos in positions)
        {
            try
            {
                var bars = await _history.GetDailyBarsAsync(pos.AssetSymbol, 252, ct);
                if (bars.Count < 15) continue;
                technicals.Add(TechnicalCalculator.Calculate(bars, pos.AverageCost));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Teknik veri alınamadı: {Symbol}", pos.AssetSymbol);
            }
        }

        var (system, user) = BuildPrompts(portfolio.Name, positions, cash, snapshots, technicals);
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
        List<PortfolioValueSnapshot> snapshots,
        List<TechnicalIndicators> technicals)
    {
        var system = """
Sen 15+ yıl deneyimli, kurumsal bir portföy yöneticisi ve risk analistisin (CFA/CMT seviyesi). Kullanıcının ABD hisse portföyünü bütünsel değerlendir: hem TEKNİK durum (trend, momentum, volatilite) hem RİSK YÖNETİMİ (konsantrasyon, nakit, korelasyon/tema yoğunlaşması) hem de PERFORMANS. Amacın yüzeysel gözlem değil, profesyonel karar desteği.

ELİNDEKİ VERİLER (her pozisyon için): fiyat, maliyet, K/Z%, portföy ağırlığı, günlük değişim, tema/sektör + teknik göstergeler (RSI, EMA21/50, SMA200, MACD histogram, ADX trend gücü, ATR% volatilite, trend durumu, TC skoru, 5g/20g getiri). Ayrıca portföy toplamı, nakit oranı, tema dağılımı ve 30 günlük getiri.

ANALİZ ÇERÇEVESİ:
- KONSANTRASYON RİSKİ: Tek isim veya tek temada aşırı ağırlık (örn. >%25) en kritik risktir, mutlaka değerlendir.
- TEKNİK SAĞLIK: Kaç pozisyon yükseliş trendinde (EMA dizilimi + ADX), kaç tanesi kırılgan (EMA50 altı + negatif MACD). Portföyün genel teknik dengesi.
- VOLATİLİTE: Yüksek ATR% olan pozisyonlar portföy riskini artırır.
- NAKİT STRATEJİSİ: Nakit oranı savunma/saldırı kapasitesini belirler (≥%15 savunmacı, %5-15 nötr, <%5 agresif).
- SENTEZ: Tek göstergeye değil, confluence'a (çoklu teyit) bak.

ÇIKTI FORMATI: Yanıtın MUTLAKA aşağıdaki JSON şemasında olsun (başka metin yok):

{
  "summary": "3-4 cümle Türkçe bütünsel özet: portföyün teknik dengesi, ana risk, genel duruş.",
  "warnings": [{ "severity": "high|med|low", "title": "...", "detail": "..." }],
  "suggestions": [{ "priority": "high|med|low", "action": "...", "reasoning": "..." }],
  "marketContext": "Portföyün genel risk profili ve önerilen tavır, 2-3 cümle."
}

KURALLAR:
- Maksimum 6 uyarı, 6 öneri.
- SPESİFİK & SAYISAL ol: "diversifikasyona dikkat" DEĞİL → "NVDA %34 ağırlık + MACD negatife döndü; $206 altı kapanışta yarım azalt" gibi seviye + gerekçe ver.
- Uyarıları confluence ile kur (en az 2 sinyal). Önerilerde reasoning'de göstergeleri say.
- Konsantrasyon, tema yoğunlaşması ve en kırılgan pozisyonları öncelikle ele al.
- Dürüst ol: sinyaller karışıksa "bekle/izle" de, aşırı iddialı olma.
- severity ve priority SADECE "high", "med" veya "low" olabilir. Türkçe yaz, sayılarla konuş.
""";

        var totalMv = positions.Sum(p => p.MarketValue ?? 0m);
        var totalEquity = totalMv + cash.Amount;
        var cashPct = totalEquity > 0 ? Math.Round(cash.Amount / totalEquity * 100m, 1) : 0m;

        var posData = positions.Select(p =>
        {
            var t = technicals.FirstOrDefault(x => x.Symbol == p.AssetSymbol);
            return new
            {
                symbol = p.AssetSymbol,
                name = p.AssetName,
                theme = p.Theme,
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
                dayChangePct = p.DayChangePercent,
                // Teknik göstergeler
                rsi = t?.Rsi14,
                ema21 = t?.Ema21,
                ema50 = t?.Ema50,
                sma200 = t?.Sma200,
                macdHistogram = t?.MacdHistogram,
                adx = t?.Adx,
                atrPct = t?.AtrPct,
                trendState = t?.TrendState,
                tcScore = t?.TcScore,
                tcLabel = t?.TcLabel,
                return20dPct = t?.Return20d
            };
        }).ToList();

        // Tema dağılımı (konsantrasyon analizi için)
        var themeBreakdown = positions
            .Where(p => p.MarketValue.HasValue && p.MarketValue.Value > 0)
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Theme) ? "Sınıflandırılmamış" : p.Theme!.Trim())
            .Select(g => new
            {
                theme = g.Key,
                valuePct = totalEquity > 0 ? Math.Round(g.Sum(p => p.MarketValue!.Value) / totalEquity * 100m, 1) : 0m
            })
            .OrderByDescending(x => x.valuePct)
            .ToList();

        var topPosition = posData.OrderByDescending(p => p.weightPct ?? 0m).FirstOrDefault();

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
            cashPct,
            positionCount = positions.Count,
            topPositionWeightPct = topPosition?.weightPct,
            themeBreakdown,
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
