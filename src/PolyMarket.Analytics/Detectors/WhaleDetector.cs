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

        // Whale bought YES (side=BUY) → follow whale → BUY YES
        // Whale sold YES (side=SELL) → whale thinks NO → BUY NO
        var isBuy = trade.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase);
        var signal = isBuy ? "BUY YES" : "BUY NO";
        var whaleAction = isBuy ? "bought YES" : "sold YES";
        var sideEmoji = isBuy ? "\ud83d\udfe2" : "\ud83d\udd34";

        var description = $"\ud83d\udc33 Whale {whaleAction} for <b>${tradeValue:N0}</b>\n" +
                          $"{sideEmoji} Price: {trade.Price:F3} | Size: {trade.Size:N0} shares\n" +
                          $"Trader: {trade.TraderAddress[..8]}...\n" +
                          $"\ud83d\udca1 Signal: <b>{signal}</b> (follow the whale)";

        return new AnomalyDetected(
            Type: AnomalyType.WhaleTrade,
            MarketId: trade.MarketId,
            Description: description,
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["traderAddress"] = trade.TraderAddress,
                ["side"] = trade.Side,
                ["signal"] = signal,
                ["size"] = trade.Size,
                ["price"] = trade.Price,
                ["tradeValue"] = tradeValue
            },
            Timestamp: trade.Timestamp);
    }
}
