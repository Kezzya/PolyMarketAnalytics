using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class WhaleDetector
{
    private const decimal WhaleThreshold = 10_000m;

    public AnomalyDetected? Detect(LargeTradeDetected trade)
    {
        var tradeValue = trade.Size * trade.Price;

        if (tradeValue < WhaleThreshold)
            return null;

        var severity = Math.Min(tradeValue / 100_000m, 1m);

        return new AnomalyDetected(
            Type: AnomalyType.WhaleTrade,
            MarketId: trade.MarketId,
            Description: $"Whale trade: {trade.Side} ${tradeValue:N0} by {trade.TraderAddress[..8]}...",
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["traderAddress"] = trade.TraderAddress,
                ["side"] = trade.Side,
                ["size"] = trade.Size,
                ["price"] = trade.Price,
                ["tradeValue"] = tradeValue
            },
            Timestamp: trade.Timestamp);
    }
}
