using PolyMarket.Contracts.Messages;

namespace PolyMarket.AutoBet.Strategy;

public class AutoBetStrategy
{
    private readonly ILogger<AutoBetStrategy> _logger;
    private readonly decimal _maxBetSize;
    private readonly decimal _minSeverity;
    private readonly decimal _minROI;
    private readonly TimeSpan _cooldownPerMarket;

    private readonly Dictionary<string, DateTime> _lastBetTime = new();

    public AutoBetStrategy(ILogger<AutoBetStrategy> logger, IConfiguration config)
    {
        _logger = logger;
        _maxBetSize = decimal.Parse(config["AutoBet:MaxBetSize"] ?? "20");
        _minSeverity = decimal.Parse(config["AutoBet:MinSeverity"] ?? "0.5");
        _minROI = decimal.Parse(config["AutoBet:MinROI"] ?? "0.30");
        _cooldownPerMarket = TimeSpan.FromSeconds(
            int.Parse(config["AutoBet:CooldownSeconds"] ?? "300"));
    }

    public BetDecision? Evaluate(AnomalyDetected anomaly)
    {
        // Only act on types that have clear BUY signals from detectors
        if (anomaly.Type != AnomalyType.WhaleTrade &&
            anomaly.Type != AnomalyType.OrderBookImbalance &&
            anomaly.Type != AnomalyType.PriceSpike &&
            anomaly.Type != AnomalyType.CryptoDivergence)
            return null;

        // Minimum severity filter
        if (anomaly.Severity < _minSeverity)
            return null;

        // Detectors now put "signal" in Details — require it
        if (!anomaly.Details.TryGetValue("signal", out var signalObj))
            return null;

        var signal = signalObj?.ToString() ?? "";
        if (signal != "BUY YES" && signal != "BUY NO")
            return null;

        // Check ROI meets minimum threshold
        if (anomaly.Details.TryGetValue("maxROI", out var roiObj) ||
            anomaly.Details.TryGetValue("expectedROI", out roiObj))
        {
            var roi = Convert.ToDecimal(roiObj);
            if (roi < _minROI)
            {
                _logger.LogDebug("ROI {ROI:P0} below minimum {MinROI:P0} for {MarketId}",
                    roi, _minROI, anomaly.MarketId);
                return null;
            }
        }

        // Cooldown — don't spam same market
        if (_lastBetTime.TryGetValue(anomaly.MarketId, out var lastTime)
            && DateTime.UtcNow - lastTime < _cooldownPerMarket)
        {
            _logger.LogDebug("Cooldown active for {MarketId}, skipping", anomaly.MarketId);
            return null;
        }

        // Determine side and price from the detector's output
        // BUY YES → side=BUY on Polymarket CLOB
        // BUY NO  → side=SELL on Polymarket CLOB (sell YES = buy NO)
        var side = signal == "BUY YES" ? "BUY" : "SELL";

        var price = GetDecimalDetail(anomaly.Details, "buyPrice")
                    ?? GetDecimalDetail(anomaly.Details, "price")
                    ?? GetDecimalDetail(anomaly.Details, "yesPrice")
                    ?? 0m;

        // For BUY NO, we need the NO price for sizing
        if (signal == "BUY NO")
        {
            price = GetDecimalDetail(anomaly.Details, "noPrice")
                    ?? (price > 0 ? 1.0m - price : 0m);
        }

        if (price <= 0 || price >= 1)
            return null;

        // ═══════════════════════════════════════════════
        // BET SIZING based on signal quality
        // ═══════════════════════════════════════════════
        //
        // Base: severity * maxBetSize
        // Multipliers:
        //   Big whale ($50k+) → 1.5x (they likely know something)
        //   Reversal play     → 1.2x (high conviction dip buy)
        //   Momentum play     → 1.0x (following trend, standard)
        //   Order book        → 0.8x (weaker signal)
        //
        // Clamp: min $5, max = maxBetSize

        var betSize = _maxBetSize * anomaly.Severity;

        var strategy = anomaly.Details.TryGetValue("strategy", out var stratObj)
            ? stratObj?.ToString() ?? "" : "";

        switch (strategy)
        {
            case "whale-follow":
                var isBigWhale = anomaly.Details.TryGetValue("isBigWhale", out var bw) && bw is true;
                betSize *= isBigWhale ? 1.5m : 1.0m;
                break;
            case "reversal":
                betSize *= 1.2m;
                break;
            case "momentum":
                betSize *= 1.0m;
                break;
            case "order-book-pressure":
                betSize *= 0.8m;
                break;
            case "crypto-arbitrage":
                // Highest conviction — math-based edge, scale with edge size
                var edge = GetDecimalDetail(anomaly.Details, "absEdge") ?? 0.05m;
                betSize *= edge >= 0.10m ? 1.5m : 1.2m;
                break;
        }

        betSize = Math.Round(Math.Max(5m, Math.Min(betSize, _maxBetSize)), 2);

        _logger.LogInformation(
            "{Signal} ${BetSize} on {MarketId} | Strategy: {Strategy} | Severity: {Severity:F2}",
            signal, betSize, anomaly.MarketId, strategy, anomaly.Severity);

        return new BetDecision(
            MarketId: anomaly.MarketId,
            Side: side,
            Size: betSize,
            Price: price,
            Reason: $"{signal} via {strategy} (severity {anomaly.Severity:F2})");
    }

    public void RecordBet(string marketId)
    {
        _lastBetTime[marketId] = DateTime.UtcNow;
    }

    private static decimal? GetDecimalDetail(Dictionary<string, object> details, string key)
    {
        if (details.TryGetValue(key, out var val))
        {
            try { return Convert.ToDecimal(val); }
            catch { return null; }
        }
        return null;
    }
}

public record BetDecision(
    string MarketId,
    string Side,
    decimal Size,
    decimal Price,
    string Reason);
