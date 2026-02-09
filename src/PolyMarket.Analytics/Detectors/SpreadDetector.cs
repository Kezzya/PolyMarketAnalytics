using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class SpreadDetector
{
    private const decimal WideSpreadThreshold = 0.10m; // 10%+ spread is truly anomalous on Polymarket
    private const decimal SpreadSpikeMultiplier = 3m;

    private readonly Dictionary<string, decimal> _avgSpreads = new();

    public void UpdateAverage(string marketId, decimal spread)
    {
        if (_avgSpreads.TryGetValue(marketId, out var current))
            _avgSpreads[marketId] = current * 0.9m + spread * 0.1m;
        else
            _avgSpreads[marketId] = spread;
    }

    public AnomalyDetected? Detect(OrderBookUpdated book)
    {
        if (book.Spread <= 0)
            return null;

        // Check 1: absolute wide spread
        var isWide = book.Spread >= WideSpreadThreshold;

        // Check 2: spread spike relative to average
        var isSpiked = false;
        if (_avgSpreads.TryGetValue(book.MarketId, out var avgSpread) && avgSpread > 0)
            isSpiked = book.Spread / avgSpread >= SpreadSpikeMultiplier;

        if (!isWide && !isSpiked)
            return null;

        var reason = (isWide, isSpiked) switch
        {
            (true, true) => "Wide spread + spike",
            (true, false) => "Wide spread",
            (false, true) => "Spread spike",
            _ => "Spread anomaly"
        };

        var severity = isWide
            ? Math.Min(book.Spread / 0.15m, 1m)
            : Math.Min((book.Spread / avgSpread) / 10m, 1m);

        return new AnomalyDetected(
            Type: AnomalyType.SpreadAnomaly,
            MarketId: book.MarketId,
            Description: $"{reason}: spread={book.Spread:F4} (avg={avgSpread:F4}), bid={book.BestBid:F4} ask={book.BestAsk:F4}",
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["spread"] = book.Spread,
                ["avgSpread"] = avgSpread,
                ["bestBid"] = book.BestBid,
                ["bestAsk"] = book.BestAsk,
                ["reason"] = reason
            },
            Timestamp: book.Timestamp);
    }
}
