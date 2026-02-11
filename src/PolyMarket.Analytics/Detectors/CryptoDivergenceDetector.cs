using PolyMarket.Analytics.Services;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

/// <summary>
/// Detects profitable divergences between Polymarket YES price
/// and the mathematically "fair" probability based on real crypto prices.
///
/// Example:
///   Market: "BTC above $110k by March 31"
///   BTC now: $98,000 | Polymarket YES: $0.35 | Fair value: $0.52
///   Edge: +17¢ → BUY YES
/// </summary>
public class CryptoDivergenceDetector
{
    private readonly FairValueCalculator _calculator;
    private readonly ILogger<CryptoDivergenceDetector> _logger;

    // Minimum edge to trigger signal (absolute probability difference)
    private const decimal MinEdge = 0.05m;         // 5% minimum edge
    private const decimal StrongEdge = 0.10m;       // 10%+ = strong signal
    private const decimal MinROI = 0.15m;            // 15% minimum expected ROI

    // Value zone — skip markets where price is too extreme
    private const decimal MinPrice = 0.05m;          // below 5¢ = too speculative
    private const decimal MaxPrice = 0.90m;          // above 90¢ = no room

    public CryptoDivergenceDetector(
        FairValueCalculator calculator,
        ILogger<CryptoDivergenceDetector> logger)
    {
        _calculator = calculator;
        _logger = logger;
    }

    public AnomalyDetected? Detect(
        string marketId,
        string question,
        decimal yesPrice,
        CryptoMarketMatch match,
        decimal currentCryptoPrice,
        decimal volatility)
    {
        // Need an expiry date for the model
        if (match.ExpiryDate is null)
            return null;

        // Skip extreme prices
        if (yesPrice < MinPrice || yesPrice > MaxPrice)
            return null;

        // Skip short-dated markets (< 2 days) — model unreliable for very short timeframes
        var daysLeft = (match.ExpiryDate.Value - DateTime.UtcNow).TotalDays;
        if (daysLeft < 2)
        {
            _logger.LogDebug("Skipping {Symbol} ${Target} — too short ({Days:F1} days left)",
                match.Symbol, match.TargetPrice, daysLeft);
            return null;
        }

        // Sanity check: volatility must be reasonable (10%-200%)
        volatility = Math.Max(0.10m, Math.Min(volatility, 2.0m));

        // Calculate fair value
        var fairResult = match.IsAbove
            ? _calculator.Calculate(currentCryptoPrice, match.TargetPrice, volatility, match.ExpiryDate.Value)
            : _calculator.CalculateBelow(currentCryptoPrice, match.TargetPrice, volatility, match.ExpiryDate.Value);

        var fairValue = fairResult.FairProbability;

        _logger.LogInformation(
            "CALC: {Symbol} {Dir} ${Target} | Price=${CryptoPrice:N2} | Vol={Vol:P0} | Days={Days:F0} | Fair={Fair:P1} | Market={Market:P1}",
            match.Symbol, match.IsAbove ? "above" : "below", match.TargetPrice,
            currentCryptoPrice, volatility, daysLeft, fairValue, yesPrice);

        // Edge = fair value - market price
        // Positive edge → market is CHEAP → BUY YES
        // Negative edge → market is EXPENSIVE → BUY NO
        var yesEdge = fairValue - yesPrice;
        var absEdge = Math.Abs(yesEdge);

        if (absEdge < MinEdge)
            return null;

        // Determine signal
        string signal;
        decimal buyPrice;
        decimal expectedROI;

        if (yesEdge > 0)
        {
            // Market underpriced → BUY YES
            signal = "BUY YES";
            buyPrice = yesPrice;
            expectedROI = yesEdge / yesPrice;  // edge relative to cost
        }
        else
        {
            // Market overpriced → BUY NO
            signal = "BUY NO";
            var noPrice = 1.0m - yesPrice;
            buyPrice = noPrice;
            var noFairValue = 1.0m - fairValue;
            expectedROI = (noFairValue - noPrice) / noPrice;
        }

        // Check minimum ROI
        if (expectedROI < MinROI)
            return null;

        var isStrong = absEdge >= StrongEdge;
        var severity = Math.Min(absEdge / 0.15m, 1.0m);  // 15% edge = severity 1.0

        var daysToExpiry = (match.ExpiryDate.Value - DateTime.UtcNow).TotalDays;

        var distancePercent = match.IsAbove
            ? (match.TargetPrice - currentCryptoPrice) / currentCryptoPrice * 100
            : (currentCryptoPrice - match.TargetPrice) / currentCryptoPrice * 100;

        var signalStrength = isStrong ? "\ud83d\udfe2 STRONG" : "\ud83d\udfe1 MODERATE";
        var directionWord = match.IsAbove ? "above" : "below";

        var description =
            $"\ud83d\udcca CRYPTO ARBITRAGE: {match.Symbol} {directionWord} ${match.TargetPrice:N0}\n" +
            $"{signalStrength} signal | Edge: <b>{absEdge:P1}</b>\n" +
            $"\n" +
            $"\ud83d\udcb0 {match.Symbol} now: <b>${currentCryptoPrice:N2}</b> (target ${match.TargetPrice:N0}, {distancePercent:+0.0;-0.0}% away)\n" +
            $"\ud83d\udcca Fair value: <b>{fairValue:P0}</b> vs Market: <b>{yesPrice:P0}</b>\n" +
            $"\u23f0 Expiry: {match.ExpiryDate.Value:MMM dd} ({daysToExpiry:F0} days)\n" +
            $"\ud83c\udfb2 Volatility: {volatility:P0} annualized\n" +
            $"\n" +
            $"\ud83d\udca1 Signal: <b>{signal}</b> at ${buyPrice:F2} (expected ROI: <b>+{expectedROI:P0}</b>)";

        _logger.LogInformation(
            "Crypto divergence: {Symbol} {Direction} ${Target} | Fair={Fair:P1} Market={Market:P1} Edge={Edge:P1}",
            match.Symbol, directionWord, match.TargetPrice, fairValue, yesPrice, yesEdge);

        return new AnomalyDetected(
            Type: AnomalyType.ArbitrageOpportunity,
            MarketId: marketId,
            Description: description,
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["signal"] = signal,
                ["strategy"] = "crypto-arbitrage",
                ["symbol"] = match.Symbol,
                ["currentCryptoPrice"] = currentCryptoPrice,
                ["targetPrice"] = match.TargetPrice,
                ["isAbove"] = match.IsAbove,
                ["yesPrice"] = yesPrice,
                ["fairValue"] = fairValue,
                ["edge"] = yesEdge,
                ["absEdge"] = absEdge,
                ["expectedROI"] = expectedROI,
                ["buyPrice"] = buyPrice,
                ["volatility"] = volatility,
                ["daysToExpiry"] = daysToExpiry,
                ["expiryDate"] = match.ExpiryDate.Value.ToString("O"),
                ["question"] = question
            },
            Timestamp: DateTime.UtcNow);
    }
}
