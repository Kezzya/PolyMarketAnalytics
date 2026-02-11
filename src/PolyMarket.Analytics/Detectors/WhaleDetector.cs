using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class WhaleDetector
{
    private const decimal WhaleThreshold = 10_000m;
    private const decimal BigWhaleThreshold = 50_000m;

    // Value zones — only follow whale when price gives good risk/reward
    private const decimal MinYesPrice = 0.08m;   // below 8¢ = too speculative
    private const decimal MaxYesPrice = 0.70m;    // above 70¢ = not enough upside
    private const decimal MinNoPrice = 0.08m;     // NO below 8¢ = not enough upside

    public AnomalyDetected? Detect(LargeTradeDetected trade)
    {
        var tradeValue = trade.Size * trade.Price;

        if (tradeValue < WhaleThreshold)
            return null;

        var price = trade.Price;
        var isBuy = trade.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase);

        // ═══════════════════════════════════════════════
        // WHALE BOUGHT YES → follow only if YES is cheap
        // ═══════════════════════════════════════════════
        //
        // Whale buying YES at $0.25 → they expect YES to resolve
        // Our edge: whale has insider info / better models
        // Risk/reward: buy YES at $0.25, max profit $0.75 per share (+300%)
        //
        // Skip if YES > 70¢ (not enough upside to justify following)

        if (isBuy && price >= MinYesPrice && price <= MaxYesPrice)
        {
            var maxProfit = 1.0m - price;                    // profit if resolves YES
            var maxROI = maxProfit / price;                   // ROI %
            var isBigWhale = tradeValue >= BigWhaleThreshold;

            // Big whale ($50k+) → lower ROI threshold (they know something)
            var minROI = isBigWhale ? 0.30m : 0.50m;
            if (maxROI < minROI)
                return null;

            var severity = Math.Min(tradeValue / 100_000m, 1m);
            var whaleSize = isBigWhale ? "\ud83d\udc0b MEGA WHALE" : "\ud83d\udc33 Whale";

            var description = $"{whaleSize} bought YES for <b>${tradeValue:N0}</b>\n" +
                              $"\ud83d\udfe2 Price: <b>${price:F3}</b> | Size: {trade.Size:N0} shares\n" +
                              $"\ud83c\udfaf Max profit if YES: <b>+{maxROI:P0}</b> (${maxProfit:F2}/share)\n" +
                              $"\ud83d\udca1 Signal: <b>BUY YES</b> (follow the whale)";

            return new AnomalyDetected(
                Type: AnomalyType.WhaleTrade,
                MarketId: trade.MarketId,
                Description: description,
                Severity: severity,
                Details: new Dictionary<string, object>
                {
                    ["traderAddress"] = trade.TraderAddress,
                    ["side"] = trade.Side,
                    ["signal"] = "BUY YES",
                    ["strategy"] = "whale-follow",
                    ["size"] = trade.Size,
                    ["price"] = price,
                    ["tradeValue"] = tradeValue,
                    ["maxROI"] = maxROI,
                    ["isBigWhale"] = isBigWhale
                },
                Timestamp: trade.Timestamp);
        }

        // ═══════════════════════════════════════════════
        // WHALE SOLD YES → contrarian: buy NO if NO is cheap
        // ═══════════════════════════════════════════════
        //
        // Whale dumping YES at $0.80 → they think NO wins
        // NO price = 1 - YES price ≈ $0.20 → good buy
        // Risk/reward: buy NO at $0.20, max profit $0.80 per share (+400%)
        //
        // Skip if NO too expensive (YES too low → NO > 92¢)

        if (!isBuy)
        {
            var noPrice = 1.0m - price;  // approximate NO price
            if (noPrice >= MinNoPrice && noPrice <= 0.70m)
            {
                var maxProfit = 1.0m - noPrice;
                var maxROI = maxProfit / noPrice;
                var isBigWhale = tradeValue >= BigWhaleThreshold;

                var minROI = isBigWhale ? 0.30m : 0.50m;
                if (maxROI < minROI)
                    return null;

                var severity = Math.Min(tradeValue / 100_000m, 1m);
                var whaleSize = isBigWhale ? "\ud83d\udc0b MEGA WHALE" : "\ud83d\udc33 Whale";

                var description = $"{whaleSize} dumped YES for <b>${tradeValue:N0}</b>\n" +
                                  $"\ud83d\udd34 YES price: <b>${price:F3}</b> \u2192 NO \u2248 <b>${noPrice:F2}</b>\n" +
                                  $"\ud83c\udfaf Max profit if NO: <b>+{maxROI:P0}</b> (${maxProfit:F2}/share)\n" +
                                  $"\ud83d\udca1 Signal: <b>BUY NO</b> (whale dumping YES)";

                return new AnomalyDetected(
                    Type: AnomalyType.WhaleTrade,
                    MarketId: trade.MarketId,
                    Description: description,
                    Severity: severity,
                    Details: new Dictionary<string, object>
                    {
                        ["traderAddress"] = trade.TraderAddress,
                        ["side"] = trade.Side,
                        ["signal"] = "BUY NO",
                        ["strategy"] = "whale-follow",
                        ["size"] = trade.Size,
                        ["price"] = price,
                        ["noPrice"] = noPrice,
                        ["tradeValue"] = tradeValue,
                        ["maxROI"] = maxROI,
                        ["isBigWhale"] = isBigWhale
                    },
                    Timestamp: trade.Timestamp);
            }
        }

        // Whale trade outside value zone → skip (bad risk/reward)
        return null;
    }
}
