using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class PriceSpikeDetector
{
    private const decimal SpikeThresholdPercent = 5m;

    public AnomalyDetected? Detect(MarketPriceChanged priceChange)
    {
        if (priceChange.ChangePercent < SpikeThresholdPercent)
            return null;

        var severity = Math.Min(priceChange.ChangePercent / 20m, 1m);

        return new AnomalyDetected(
            Type: AnomalyType.PriceSpike,
            MarketId: priceChange.MarketId,
            Description: $"Price spike: {priceChange.OldPrice:F4} -> {priceChange.NewPrice:F4} ({priceChange.ChangePercent:F1}%)",
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["oldPrice"] = priceChange.OldPrice,
                ["newPrice"] = priceChange.NewPrice,
                ["changePercent"] = priceChange.ChangePercent,
                ["question"] = priceChange.Question
            },
            Timestamp: priceChange.Timestamp);
    }
}
