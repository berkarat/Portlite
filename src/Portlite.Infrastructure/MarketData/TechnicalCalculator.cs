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
    string TcLabel);          // Güçlü / Sağlıklı / Baskı / Zayıf / Bitmiş

public static class TechnicalCalculator
{
    public static TechnicalIndicators Calculate(List<DailyBar> bars, decimal avgCost)
    {
        if (bars.Count < 15)
            throw new InvalidOperationException($"En az 15 bar gerekli, {bars.Count} bar geldi.");

        var closes = bars.Select(b => b.Close).ToList();
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

        var tcScore = CalculateTcScore(latest, ema50, sma200, rsi14, avgCost, high52);
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
            TcLabel: tcLabel);
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
    /// TrendCheck (TC) skor - basit kurallar:
    /// - Fiyat > 200MA → +30
    /// - Fiyat > 50MA → +25
    /// - RSI 40-70 (sağlıklı bölge) → +20
    /// - Fiyat > Maliyet → +15
    /// - 52H zirvesinin %10'u içinde → +10
    /// </summary>
    private static int CalculateTcScore(decimal price, decimal ema50, decimal sma200, decimal rsi, decimal avgCost, decimal high52)
    {
        int score = 0;

        if (price > sma200) score += 30;
        if (price > ema50) score += 25;
        if (rsi >= 40m && rsi <= 70m) score += 20;
        else if (rsi > 70m) score += 10; // aşırı alımda kısmen puan
        if (price > avgCost) score += 15;
        if (high52 > 0 && (high52 - price) / high52 <= 0.10m) score += 10;

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
