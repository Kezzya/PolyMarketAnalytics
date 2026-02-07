using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class VolumeSpikeDetector
{
    private const decimal SpikeMultiplierThreshold = 3m;
    private readonly Dictionary<string, decimal> _averageVolumes = new();

    public void UpdateAverage(string marketId, decimal volume24h)
    {
        if (_averageVolumes.TryGetValue(marketId, out var current))
        {
            _averageVolumes[marketId] = current * 0.9m + volume24h * 0.1m;
        }
        else
        {
            _averageVolumes[marketId] = volume24h;
        }
    }

    public AnomalyDetected? Detect(MarketSnapshotUpdated snapshot)
    {
        if (!_averageVolumes.TryGetValue(snapshot.MarketId, out var avgVolume) || avgVolume <= 0)
            return null;

        var multiplier = snapshot.Volume24h / avgVolume;

        if (multiplier < SpikeMultiplierThreshold)
            return null;

        var severity = Math.Min(multiplier / 10m, 1m);

        return new AnomalyDetected(
            Type: AnomalyType.VolumeSpike,
            MarketId: snapshot.MarketId,
            Description: $"Volume spike: {multiplier:F1}x normal volume",
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["normalVolume"] = avgVolume,
                ["currentVolume"] = snapshot.Volume24h,
                ["spikeMultiplier"] = multiplier,
                ["question"] = snapshot.Question
            },
            Timestamp: snapshot.Timestamp);
    }
}
