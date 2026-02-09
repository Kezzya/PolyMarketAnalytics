using PolyMarket.Contracts.Messages;

namespace PolyMarket.AutoBet.Strategy;

public class AutoBetStrategy
{
    private readonly ILogger<AutoBetStrategy> _logger;
    private readonly decimal _maxBetSize;
    private readonly decimal _minSeverity;
    private readonly TimeSpan _cooldownPerMarket;

    private readonly Dictionary<string, DateTime> _lastBetTime = new();

    public AutoBetStrategy(ILogger<AutoBetStrategy> logger, IConfiguration config)
    {
        _logger = logger;
        _maxBetSize = decimal.Parse(config["AutoBet:MaxBetSize"] ?? "20");
        _minSeverity = decimal.Parse(config["AutoBet:MinSeverity"] ?? "0.5");
        _cooldownPerMarket = TimeSpan.FromSeconds(
            int.Parse(config["AutoBet:CooldownSeconds"] ?? "300"));
    }

    public BetDecision? Evaluate(AnomalyDetected anomaly)
    {
        // Only act on whale trades and order book imbalance
        if (anomaly.Type != AnomalyType.WhaleTrade &&
            anomaly.Type != AnomalyType.OrderBookImbalance)
            return null;

        // Minimum severity filter
        if (anomaly.Severity < _minSeverity)
            return null;

        // Cooldown — don't spam same market
        if (_lastBetTime.TryGetValue(anomaly.MarketId, out var lastTime)
            && DateTime.UtcNow - lastTime < _cooldownPerMarket)
        {
            _logger.LogDebug("Cooldown active for {MarketId}, skipping", anomaly.MarketId);
            return null;
        }

        return anomaly.Type switch
        {
            AnomalyType.WhaleTrade => EvaluateWhaleTrade(anomaly),
            AnomalyType.OrderBookImbalance => EvaluateImbalance(anomaly),
            _ => null
        };
    }

    public void RecordBet(string marketId)
    {
        _lastBetTime[marketId] = DateTime.UtcNow;
    }

    private BetDecision? EvaluateWhaleTrade(AnomalyDetected anomaly)
    {
        // Follow the whale — same side as their trade
        var side = anomaly.Details.TryGetValue("side", out var sideObj)
            ? sideObj?.ToString()?.ToUpperInvariant() ?? "BUY"
            : "BUY";

        var price = anomaly.Details.TryGetValue("price", out var priceObj)
            ? Convert.ToDecimal(priceObj)
            : 0m;

        if (price <= 0 || price >= 1)
            return null;

        // Scale bet size by severity ($10-$50 range)
        var betSize = Math.Round(_maxBetSize * anomaly.Severity, 2);
        betSize = Math.Max(10m, Math.Min(betSize, _maxBetSize));

        _logger.LogInformation(
            "Whale signal: {Side} ${BetSize} on {MarketId} (whale severity={Severity:F2})",
            side, betSize, anomaly.MarketId, anomaly.Severity);

        return new BetDecision(
            MarketId: anomaly.MarketId,
            Side: side,
            Size: betSize,
            Price: price,
            Reason: $"Following whale {side} (severity {anomaly.Severity:F2})");
    }

    private BetDecision? EvaluateImbalance(AnomalyDetected anomaly)
    {
        // If heavy BUY imbalance → buy YES, if heavy SELL → buy NO (= sell YES)
        var direction = anomaly.Details.TryGetValue("direction", out var dirObj)
            ? dirObj?.ToString()?.ToUpperInvariant() ?? ""
            : "";

        var bestBid = anomaly.Details.TryGetValue("bestBid", out var bidObj)
            ? Convert.ToDecimal(bidObj) : 0m;
        var bestAsk = anomaly.Details.TryGetValue("bestAsk", out var askObj)
            ? Convert.ToDecimal(askObj) : 0m;

        if (bestBid <= 0 && bestAsk <= 0)
            return null;

        var side = direction == "BUY" ? "BUY" : "SELL";
        var price = side == "BUY" ? bestAsk : bestBid;

        if (price <= 0 || price >= 1)
            return null;

        var betSize = Math.Round(_maxBetSize * anomaly.Severity * 0.7m, 2);
        betSize = Math.Max(10m, Math.Min(betSize, _maxBetSize));

        _logger.LogInformation(
            "Imbalance signal: {Side} ${BetSize} on {MarketId} (imbalance direction={Dir})",
            side, betSize, anomaly.MarketId, direction);

        return new BetDecision(
            MarketId: anomaly.MarketId,
            Side: side,
            Size: betSize,
            Price: price,
            Reason: $"Order book imbalance {direction} (severity {anomaly.Severity:F2})");
    }
}

public record BetDecision(
    string MarketId,
    string Side,
    decimal Size,
    decimal Price,
    string Reason);
