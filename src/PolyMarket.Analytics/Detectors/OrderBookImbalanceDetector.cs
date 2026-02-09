using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class OrderBookImbalanceDetector
{
    private const decimal ImbalanceThreshold = 0.8m; // 80%+ imbalance = very strong signal

    private readonly Dictionary<string, decimal> _avgImbalance = new();

    public void UpdateAverage(string marketId, decimal imbalanceRatio)
    {
        if (_avgImbalance.TryGetValue(marketId, out var current))
            _avgImbalance[marketId] = current * 0.85m + Math.Abs(imbalanceRatio) * 0.15m;
        else
            _avgImbalance[marketId] = Math.Abs(imbalanceRatio);
    }

    public AnomalyDetected? Detect(OrderBookUpdated book)
    {
        var absImbalance = Math.Abs(book.ImbalanceRatio);

        if (absImbalance < ImbalanceThreshold)
            return null;

        var direction = book.ImbalanceRatio > 0 ? "BUY" : "SELL";
        var severity = Math.Min(absImbalance / 1.0m, 1m);

        return new AnomalyDetected(
            Type: AnomalyType.OrderBookImbalance,
            MarketId: book.MarketId,
            Description: $"Order book imbalance: {direction} pressure {absImbalance:P0} " +
                         $"(bid depth ${book.BidDepth:N0} vs ask depth ${book.AskDepth:N0})",
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["imbalanceRatio"] = book.ImbalanceRatio,
                ["direction"] = direction,
                ["bidDepth"] = book.BidDepth,
                ["askDepth"] = book.AskDepth,
                ["bestBid"] = book.BestBid,
                ["bestAsk"] = book.BestAsk
            },
            Timestamp: book.Timestamp);
    }
}
