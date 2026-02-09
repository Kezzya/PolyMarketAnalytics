using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class MarketDivergenceDetector
{
    private const decimal NearResolutionThreshold = 0.95m;
    private const decimal NearZeroThreshold = 0.05m;
    private const decimal DivergenceThreshold = 0.10m;

    private readonly Dictionary<string, decimal> _lastYesPrices = new();
    private readonly Dictionary<string, MarketSnapshotUpdated> _lastSnapshots = new();

    public void UpdatePrice(string marketId, decimal yesPrice)
    {
        _lastYesPrices[marketId] = yesPrice;
    }

    public void UpdateSnapshot(MarketSnapshotUpdated snapshot)
    {
        _lastSnapshots[snapshot.MarketId] = snapshot;
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

    /// <summary>
    /// Detect YES+NO price divergence within a single market.
    /// In a healthy binary market, YES+NO should sum to ~1.0.
    /// Significant deviation indicates anomaly or opportunity.
    /// </summary>
    public AnomalyDetected? DetectPriceSumDivergence(MarketSnapshotUpdated snapshot)
    {
        var sum = snapshot.YesPrice + snapshot.NoPrice;
        var deviation = Math.Abs(sum - 1.0m);

        if (deviation < DivergenceThreshold)
            return null;

        var severity = Math.Min(deviation / 0.3m, 1m);

        return new AnomalyDetected(
            Type: AnomalyType.MarketDivergence,
            MarketId: snapshot.MarketId,
            Description: $"Price sum divergence: YES={snapshot.YesPrice:F4} + NO={snapshot.NoPrice:F4} = {sum:F4} (deviation {deviation:F4})",
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["yesPrice"] = snapshot.YesPrice,
                ["noPrice"] = snapshot.NoPrice,
                ["sum"] = sum,
                ["deviation"] = deviation,
                ["question"] = snapshot.Question
            },
            Timestamp: snapshot.Timestamp);
    }

    /// <summary>
    /// Compare two related markets — e.g. if market A "Team X wins finals"
    /// contradicts market B "Team X wins semi-finals" (can't win finals without winning semis).
    /// Call this externally with pairs of related markets.
    /// </summary>
    public AnomalyDetected? DetectCrossMarketDivergence(
        string marketId1, string question1, decimal yesPrice1,
        string marketId2, string question2, decimal yesPrice2)
    {
        // If market1 is a "superset" condition, its price should be <= market2's price
        // For now, just detect large price gaps between related markets
        var gap = Math.Abs(yesPrice1 - yesPrice2);

        if (gap < DivergenceThreshold)
            return null;

        var severity = Math.Min(gap / 0.5m, 1m);

        return new AnomalyDetected(
            Type: AnomalyType.MarketDivergence,
            MarketId: marketId1,
            Description: $"Cross-market divergence: [{question1[..Math.Min(40, question1.Length)]}] YES={yesPrice1:F4} vs [{question2[..Math.Min(40, question2.Length)]}] YES={yesPrice2:F4}, gap={gap:F4}",
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["market1"] = marketId1,
                ["market2"] = marketId2,
                ["question1"] = question1,
                ["question2"] = question2,
                ["yesPrice1"] = yesPrice1,
                ["yesPrice2"] = yesPrice2,
                ["gap"] = gap
            },
            Timestamp: DateTime.UtcNow);
    }
}
