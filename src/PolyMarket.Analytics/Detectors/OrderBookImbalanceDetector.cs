using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class OrderBookImbalanceDetector
{
    private const decimal ImbalanceThreshold = 0.9m;
    private const decimal MinTotalDepth = 500m;
    private const int MinObservations = 3;

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

        // BUY side dominant (bidDepth >> askDepth) → buyers want YES → signal BUY YES
        // SELL side dominant (askDepth >> bidDepth) → sellers dumping YES → signal BUY NO
        var isBuyPressure = book.ImbalanceRatio > 0;
        var signal = isBuyPressure ? "BUY YES" : "BUY NO";
        var pressureEmoji = isBuyPressure ? "\ud83d\udfe2" : "\ud83d\udd34";

        var severity = Math.Min(absImbalance, 1m);

        var description = $"{pressureEmoji} {(isBuyPressure ? "BUY" : "SELL")} pressure {absImbalance:P0}\n" +
                          $"Bids: ${book.BidDepth:N0} vs Asks: ${book.AskDepth:N0}\n" +
                          $"Best bid: {book.BestBid:F3} / Best ask: {book.BestAsk:F3}\n" +
                          $"\ud83d\udca1 Signal: <b>{signal}</b>";

        return new AnomalyDetected(
            Type: AnomalyType.OrderBookImbalance,
            MarketId: book.MarketId,
            Description: description,
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["imbalanceRatio"] = book.ImbalanceRatio,
                ["direction"] = isBuyPressure ? "BUY" : "SELL",
                ["signal"] = signal,
                ["bidDepth"] = book.BidDepth,
                ["askDepth"] = book.AskDepth,
                ["bestBid"] = book.BestBid,
                ["bestAsk"] = book.BestAsk
            },
            Timestamp: book.Timestamp);
    }
}
