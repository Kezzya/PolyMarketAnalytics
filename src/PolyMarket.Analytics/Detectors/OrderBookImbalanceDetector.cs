using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class OrderBookImbalanceDetector
{
    private const decimal ImbalanceThreshold = 0.9m;
    private const decimal MinTotalDepth = 500m;
    private const int MinObservations = 3;

    // Value zone — only signal when price gives good risk/reward
    private const decimal MinYesPrice = 0.08m;
    private const decimal MaxYesPrice = 0.70m;

    private readonly Dictionary<string, decimal> _avgImbalance = new();
    private readonly Dictionary<string, int> _observationCount = new();

    public void UpdateAverage(string marketId, decimal imbalanceRatio)
    {
        var abs = Math.Abs(imbalanceRatio);
        if (_avgImbalance.TryGetValue(marketId, out var current))
            _avgImbalance[marketId] = current * 0.85m + abs * 0.15m;
        else
            _avgImbalance[marketId] = abs;

        _observationCount[marketId] = _observationCount.GetValueOrDefault(marketId, 0) + 1;
    }

    public AnomalyDetected? Detect(OrderBookUpdated book)
    {
        var absImbalance = Math.Abs(book.ImbalanceRatio);

        if (absImbalance < ImbalanceThreshold)
            return null;

        var totalDepth = book.BidDepth + book.AskDepth;
        if (totalDepth < MinTotalDepth)
            return null;

        if (_observationCount.GetValueOrDefault(book.MarketId, 0) < MinObservations)
            return null;

        if (_avgImbalance.TryGetValue(book.MarketId, out var avg) && avg > 0.7m)
            return null;

        // Use midpoint price as approximate YES price
        var yesPrice = (book.BestBid + book.BestAsk) / 2m;
        var isBuyPressure = book.ImbalanceRatio > 0;

        // ═══════════════════════════════════════════════
        // BUY PRESSURE → buyers want YES → signal BUY YES
        // ═══════════════════════════════════════════════
        //
        // Heavy bid side = market expects YES
        // Only signal if YES is still cheap (8¢-70¢) → room to grow
        // Risk/reward: buy at current YES price, profit if resolves YES

        if (isBuyPressure && yesPrice >= MinYesPrice && yesPrice <= MaxYesPrice)
        {
            var maxProfit = 1.0m - yesPrice;
            var maxROI = maxProfit / yesPrice;

            if (maxROI < 0.40m)  // need 40%+ potential ROI
                return null;

            var severity = Math.Min(absImbalance, 1m);

            var description = $"\ud83d\udfe2 Heavy BUY pressure ({absImbalance:P0})\n" +
                              $"Bids: ${book.BidDepth:N0} vs Asks: ${book.AskDepth:N0}\n" +
                              $"YES price: <b>${yesPrice:F3}</b>\n" +
                              $"\ud83c\udfaf Max profit if YES: <b>+{maxROI:P0}</b>\n" +
                              $"\ud83d\udca1 Signal: <b>BUY YES</b> (order book pressure)";

            return new AnomalyDetected(
                Type: AnomalyType.OrderBookImbalance,
                MarketId: book.MarketId,
                Description: description,
                Severity: severity,
                Details: new Dictionary<string, object>
                {
                    ["imbalanceRatio"] = book.ImbalanceRatio,
                    ["direction"] = "BUY",
                    ["signal"] = "BUY YES",
                    ["strategy"] = "order-book-pressure",
                    ["yesPrice"] = yesPrice,
                    ["maxROI"] = maxROI,
                    ["bidDepth"] = book.BidDepth,
                    ["askDepth"] = book.AskDepth,
                    ["bestBid"] = book.BestBid,
                    ["bestAsk"] = book.BestAsk
                },
                Timestamp: book.Timestamp);
        }

        // ═══════════════════════════════════════════════
        // SELL PRESSURE → sellers dumping YES → signal BUY NO
        // ═══════════════════════════════════════════════
        //
        // Heavy ask side = market expects NO
        // NO price ≈ 1 - YES price
        // Only signal if NO is cheap (YES > 30¢ → NO < 70¢)

        if (!isBuyPressure)
        {
            var noPrice = 1.0m - yesPrice;
            if (noPrice >= MinYesPrice && noPrice <= MaxYesPrice)
            {
                var maxProfit = 1.0m - noPrice;
                var maxROI = maxProfit / noPrice;

                if (maxROI < 0.40m)
                    return null;

                var severity = Math.Min(absImbalance, 1m);

                var description = $"\ud83d\udd34 Heavy SELL pressure ({absImbalance:P0})\n" +
                                  $"Bids: ${book.BidDepth:N0} vs Asks: ${book.AskDepth:N0}\n" +
                                  $"YES price: ${yesPrice:F3} \u2192 NO \u2248 <b>${noPrice:F2}</b>\n" +
                                  $"\ud83c\udfaf Max profit if NO: <b>+{maxROI:P0}</b>\n" +
                                  $"\ud83d\udca1 Signal: <b>BUY NO</b> (order book pressure)";

                return new AnomalyDetected(
                    Type: AnomalyType.OrderBookImbalance,
                    MarketId: book.MarketId,
                    Description: description,
                    Severity: severity,
                    Details: new Dictionary<string, object>
                    {
                        ["imbalanceRatio"] = book.ImbalanceRatio,
                        ["direction"] = "SELL",
                        ["signal"] = "BUY NO",
                        ["strategy"] = "order-book-pressure",
                        ["yesPrice"] = yesPrice,
                        ["noPrice"] = noPrice,
                        ["maxROI"] = maxROI,
                        ["bidDepth"] = book.BidDepth,
                        ["askDepth"] = book.AskDepth,
                        ["bestBid"] = book.BestBid,
                        ["bestAsk"] = book.BestAsk
                    },
                    Timestamp: book.Timestamp);
            }
        }

        // Price outside value zone → skip
        return null;
    }
}
