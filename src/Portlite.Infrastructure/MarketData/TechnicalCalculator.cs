namespace Portlite.Infrastructure.MarketData;

public record TechnicalIndicators(
    string Symbol,
    decimal CurrentPrice,
    decimal Rsi14,
    decimal Ema21,
    decimal Ema50,
    decimal Sma50,
    decimal Sma200,
    decimal High52Week,
    decimal Low52Week,
    decimal DistFrom52High,   // % distance from 52-week high (negative = below)
    decimal DistFromEma50,    // % distance from EMA50
    decimal DistFrom200Ma,    // % distance from 200MA
    int TcScore,              // TrendCheck score 0-100
    string TcLabel,           // Güçlü / Sağlıklı / Baskı / Zayıf / Bitmiş
    // ── Genişletilmiş profesyonel göstergeler ──
    decimal Macd,             // MACD çizgisi (EMA12 - EMA26)
    decimal MacdSignal,       // Sinyal çizgisi (MACD'nin EMA9'u)
    decimal MacdHistogram,    // MACD - Signal (momentum yönü)
    decimal Adx,              // Trend gücü (0-100; >25 güçlü trend)
    decimal Atr14,            // Average True Range (volatilite, $ cinsinden)
    decimal AtrPct,           // ATR / fiyat * 100 (% volatilite)
    decimal VolumeRatio,      // Son hacim / 20 günlük ort. hacim
    decimal Return5d,         // Son 5 günlük getiri %
    decimal Return20d,        // Son 20 günlük getiri %
    decimal SuggestedStop,    // ATR-bazlı önerilen stop ($)
    string TrendState);       // Yükseliş / Yatay / Düşüş (EMA dizilimi)

public static class TechnicalCalculator
{
    public static TechnicalIndicators Calculate(List<DailyBar> bars, decimal avgCost)
    {
        if (bars.Count < 15)
            throw new InvalidOperationException($"En az 15 bar gerekli, {bars.Count} bar geldi.");

        var closes = bars.Select(b => b.Close).ToList();
        var highs = bars.Select(b => b.High).ToList();
        var lows = bars.Select(b => b.Low).ToList();
        var volumes = bars.Select(b => (decimal)b.Volume).ToList();
        var latest = closes[^1];
        var symbol = bars[0].Symbol;

        var rsi14 = CalculateRsi(closes, 14);
        var sma50 = closes.Count >= 50 ? closes.TakeLast(50).Average() : closes.Average();
        var sma200 = closes.Count >= 200 ? closes.TakeLast(200).Average() : closes.Average();
        var ema21 = CalculateEma(closes, 21);
        var ema50 = CalculateEma(closes, 50);

        // 52-week = 252 trading days
        var yearBars = closes.TakeLast(Math.Min(252, closes.Count)).ToList();
        var high52 = yearBars.Max();
        var low52 = yearBars.Min();

        var distFrom52High = high52 > 0 ? (latest - high52) / high52 * 100m : 0m;
        var distFromEma50 = ema50 > 0 ? (latest - ema50) / ema50 * 100m : 0m;
        var distFrom200Ma = sma200 > 0 ? (latest - sma200) / sma200 * 100m : 0m;

        // ── Genişletilmiş göstergeler ──
        var (macd, macdSignal, macdHist) = CalculateMacd(closes);
        var adx = CalculateAdx(highs, lows, closes, 14);
        var atr = CalculateAtr(highs, lows, closes, 14);
        var atrPct = latest > 0 ? atr / latest * 100m : 0m;

        var avgVol20 = volumes.Count >= 20 ? volumes.TakeLast(20).Average() : volumes.Average();
        var volumeRatio = avgVol20 > 0 ? volumes[^1] / avgVol20 : 1m;

        var return5d = PeriodReturn(closes, 5);
        var return20d = PeriodReturn(closes, 20);

        // ATR-bazlı önerilen stop: 2x ATR altı (klasik trend stop)
        var suggestedStop = latest - 2m * atr;

        var trendState = DetermineTrend(latest, ema21, ema50, sma200);

        var tcScore = CalculateTcScore(latest, ema21, ema50, sma200, rsi14, avgCost, high52, macdHist, adx, volumeRatio);
        var tcLabel = TcScoreToLabel(tcScore);

        return new TechnicalIndicators(
            Symbol: symbol,
            CurrentPrice: latest,
            Rsi14: Math.Round(rsi14, 2),
            Ema21: Math.Round(ema21, 2),
            Ema50: Math.Round(ema50, 2),
            Sma50: Math.Round(sma50, 2),
            Sma200: Math.Round(sma200, 2),
            High52Week: high52,
            Low52Week: low52,
            DistFrom52High: Math.Round(distFrom52High, 2),
            DistFromEma50: Math.Round(distFromEma50, 2),
            DistFrom200Ma: Math.Round(distFrom200Ma, 2),
            TcScore: tcScore,
            TcLabel: tcLabel,
            Macd: Math.Round(macd, 3),
            MacdSignal: Math.Round(macdSignal, 3),
            MacdHistogram: Math.Round(macdHist, 3),
            Adx: Math.Round(adx, 1),
            Atr14: Math.Round(atr, 2),
            AtrPct: Math.Round(atrPct, 2),
            VolumeRatio: Math.Round(volumeRatio, 2),
            Return5d: Math.Round(return5d, 2),
            Return20d: Math.Round(return20d, 2),
            SuggestedStop: Math.Round(suggestedStop, 2),
            TrendState: trendState);
    }

    private static decimal PeriodReturn(List<decimal> closes, int daysBack)
    {
        if (closes.Count <= daysBack) return 0m;
        var prev = closes[^(daysBack + 1)];
        return prev > 0 ? (closes[^1] - prev) / prev * 100m : 0m;
    }

    private static string DetermineTrend(decimal price, decimal ema21, decimal ema50, decimal sma200)
    {
        // Klasik EMA dizilimi: fiyat > EMA21 > EMA50 > SMA200 → güçlü yükseliş
        if (price > ema21 && ema21 > ema50 && ema50 > sma200) return "Yükseliş";
        if (price < ema21 && ema21 < ema50 && ema50 < sma200) return "Düşüş";
        return "Yatay";
    }

    private static (decimal macd, decimal signal, decimal histogram) CalculateMacd(List<decimal> closes)
    {
        if (closes.Count < 26) return (0m, 0m, 0m);

        // MACD = EMA12 - EMA26, her bar için seri hesapla
        var ema12Series = EmaSeries(closes, 12);
        var ema26Series = EmaSeries(closes, 26);
        var macdSeries = new List<decimal>();
        for (int i = 0; i < closes.Count; i++)
            macdSeries.Add(ema12Series[i] - ema26Series[i]);

        // Sinyal = MACD'nin EMA9'u (son 26 bardan itibaren anlamlı)
        var signalSeries = EmaSeries(macdSeries, 9);
        var macd = macdSeries[^1];
        var signal = signalSeries[^1];
        return (macd, signal, macd - signal);
    }

    /// <summary>Her bar için EMA değerini döndürür (seed = ilk değer).</summary>
    private static List<decimal> EmaSeries(List<decimal> values, int period)
    {
        var result = new List<decimal>(values.Count);
        if (values.Count == 0) return result;
        var multiplier = 2m / (period + 1);
        decimal ema = values[0];
        result.Add(ema);
        for (int i = 1; i < values.Count; i++)
        {
            ema = (values[i] - ema) * multiplier + ema;
            result.Add(ema);
        }
        return result;
    }

    private static decimal CalculateAtr(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period)
    {
        if (closes.Count < 2) return 0m;
        var trs = new List<decimal>();
        for (int i = 1; i < closes.Count; i++)
        {
            var h = highs[i]; var l = lows[i]; var pc = closes[i - 1];
            var tr = Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc)));
            trs.Add(tr);
        }
        if (trs.Count == 0) return 0m;
        var take = Math.Min(period, trs.Count);
        // Wilder smoothing
        var atr = trs.Take(take).Average();
        for (int i = take; i < trs.Count; i++)
            atr = (atr * (period - 1) + trs[i]) / period;
        return atr;
    }

    private static decimal CalculateAdx(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period)
    {
        int n = closes.Count;
        if (n < period * 2) return 0m;

        var plusDM = new List<decimal>();
        var minusDM = new List<decimal>();
        var tr = new List<decimal>();
        for (int i = 1; i < n; i++)
        {
            var upMove = highs[i] - highs[i - 1];
            var downMove = lows[i - 1] - lows[i];
            plusDM.Add(upMove > downMove && upMove > 0 ? upMove : 0m);
            minusDM.Add(downMove > upMove && downMove > 0 ? downMove : 0m);
            var h = highs[i]; var l = lows[i]; var pc = closes[i - 1];
            tr.Add(Math.Max(h - l, Math.Max(Math.Abs(h - pc), Math.Abs(l - pc))));
        }

        // Wilder smoothed values
        // DX serisini hesapla
        var dxValues = new List<decimal>();
        decimal trS = tr.Take(period).Sum();
        decimal pdmS = plusDM.Take(period).Sum();
        decimal mdmS = minusDM.Take(period).Sum();
        void PushDx()
        {
            if (trS == 0) { dxValues.Add(0); return; }
            var pdi = pdmS / trS * 100m;
            var mdi = mdmS / trS * 100m;
            var sum = pdi + mdi;
            dxValues.Add(sum == 0 ? 0 : Math.Abs(pdi - mdi) / sum * 100m);
        }
        PushDx();
        for (int i = period; i < tr.Count; i++)
        {
            trS = trS - (trS / period) + tr[i];
            pdmS = pdmS - (pdmS / period) + plusDM[i];
            mdmS = mdmS - (mdmS / period) + minusDM[i];
            PushDx();
        }

        if (dxValues.Count == 0) return 0m;
        var take = Math.Min(period, dxValues.Count);
        var adx = dxValues.Take(take).Average();
        for (int i = take; i < dxValues.Count; i++)
            adx = (adx * (period - 1) + dxValues[i]) / period;
        return adx;
    }


    private static decimal CalculateEma(List<decimal> closes, int period)
    {
        if (closes.Count < period) return closes.Average();
        var multiplier = 2m / (period + 1);
        var ema = closes.Take(period).Average(); // seed with SMA
        for (int i = period; i < closes.Count; i++)
            ema = (closes[i] - ema) * multiplier + ema;
        return ema;
    }

    private static decimal CalculateRsi(List<decimal> closes, int period)
    {
        if (closes.Count < period + 1) return 50m; // not enough data

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? -change : 0);
        }

        // Initial averages (SMA-based seed)
        var avgGain = gains.Take(period).Average();
        var avgLoss = losses.Take(period).Average();

        // Smoothed (Wilder's method)
        for (int i = period; i < gains.Count; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
        }

        if (avgLoss == 0) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    /// <summary>
    /// TrendCheck (TC) skoru — çok faktörlü (0-100):
    /// - Fiyat > 200MA → +20 (uzun vade trend)
    /// - Fiyat > 50MA → +15 (orta vade trend)
    /// - Fiyat > 21EMA → +10 (kısa vade momentum)
    /// - RSI 40-70 sağlıklı bölge → +15 (aşırı alımda +7)
    /// - MACD histogramı pozitif → +12 (momentum yukarı)
    /// - ADX > 25 (güçlü trend) → +10
    /// - Fiyat > maliyet → +8
    /// - 52H zirvesinin %10 içinde → +6
    /// - Hacim ortalamanın üstünde (ratio > 1.1) → +4
    /// </summary>
    private static int CalculateTcScore(
        decimal price, decimal ema21, decimal ema50, decimal sma200,
        decimal rsi, decimal avgCost, decimal high52,
        decimal macdHist, decimal adx, decimal volumeRatio)
    {
        int score = 0;

        if (price > sma200) score += 20;
        if (price > ema50) score += 15;
        if (price > ema21) score += 10;

        if (rsi >= 40m && rsi <= 70m) score += 15;
        else if (rsi > 70m) score += 7; // aşırı alımda kısmi puan

        if (macdHist > 0) score += 12;
        if (adx >= 25m) score += 10;
        else if (adx >= 20m) score += 5;

        if (avgCost > 0 && price > avgCost) score += 8;
        if (high52 > 0 && (high52 - price) / high52 <= 0.10m) score += 6;
        if (volumeRatio > 1.1m) score += 4;

        return Math.Min(score, 100);
    }

    private static string TcScoreToLabel(int score) => score switch
    {
        >= 85 => "Güçlü",
        >= 65 => "Sağlıklı",
        >= 50 => "Baskı",
        >= 35 => "Zayıf",
        _ => "Bitmiş"
    };
}
