using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class MarketDivergenceDetector
{
    private const decimal NearResolutionThreshold = 0.95m;
    private const decimal NearZeroThreshold = 0.05m;

    private readonly Dictionary<string, decimal> _lastYesPrices = new();

    public void UpdatePrice(string marketId, decimal yesPrice)
    {
        _lastYesPrices[marketId] = yesPrice;
    }

    public AnomalyDetected? DetectNearResolution(MarketSnapshotUpdated snapshot)
    {
        if (snapshot.YesPrice < NearResolutionThreshold && snapshot.YesPrice > NearZeroThreshold)
            return null;

        var isHigh = snapshot.YesPrice >= NearResolutionThreshold;
        var severity = isHigh
            ? (snapshot.YesPrice - NearResolutionThreshold) / (1m - NearResolutionThreshold)
            : (NearZeroThreshold - snapshot.YesPrice) / NearZeroThreshold;

        severity = Math.Min(Math.Max(severity, 0.3m), 1m);

        return new AnomalyDetected(
            Type: AnomalyType.NearResolution,
            MarketId: snapshot.MarketId,
            Description: $"Market near resolution: YES={snapshot.YesPrice:F4} ({(isHigh ? "likely YES" : "likely NO")})",
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["yesPrice"] = snapshot.YesPrice,
                ["noPrice"] = snapshot.NoPrice,
                ["question"] = snapshot.Question,
                ["direction"] = isHigh ? "YES" : "NO"
            },
            Timestamp: snapshot.Timestamp);
    }
}
