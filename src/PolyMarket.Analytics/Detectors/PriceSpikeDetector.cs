using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class PriceSpikeDetector
{
    private const decimal SpikeThresholdPercent = 10m;
    private const decimal MinPriceForAlert = 0.05m;

    public AnomalyDetected? Detect(MarketPriceChanged priceChange)
    {
        if (priceChange.ChangePercent < SpikeThresholdPercent)
            return null;

        if (priceChange.OldPrice < MinPriceForAlert && priceChange.NewPrice < MinPriceForAlert)
            return null;

        var severity = Math.Min(priceChange.ChangePercent / 20m, 1m);

        var isUp = priceChange.NewPrice > priceChange.OldPrice;
        var direction = isUp ? "UP" : "DOWN";
        var dirEmoji = isUp ? "\ud83d\udcc8" : "\ud83d\udcc9";
        var yesPercent = priceChange.NewPrice * 100;
        var noPercent = (1 - priceChange.NewPrice) * 100;

        // Signal: if YES price going UP → market thinks YES more likely → BUY YES
        //         if YES price going DOWN → market thinks NO more likely → BUY NO
        var signal = isUp ? "BUY YES" : "BUY NO";

        var description = $"{dirEmoji} YES price {direction}: {priceChange.OldPrice:F2} \u2192 {priceChange.NewPrice:F2} ({priceChange.ChangePercent:F1}%)\n" +
                          $"Current: YES {yesPercent:F0}% / NO {noPercent:F0}%\n" +
                          $"\ud83d\udca1 Signal: <b>{signal}</b>";

        return new AnomalyDetected(
            Type: AnomalyType.PriceSpike,
            MarketId: priceChange.MarketId,
            Description: description,
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["oldPrice"] = priceChange.OldPrice,
                ["newPrice"] = priceChange.NewPrice,
                ["changePercent"] = priceChange.ChangePercent,
                ["direction"] = direction,
                ["signal"] = signal,
                ["question"] = priceChange.Question
            },
            Timestamp: priceChange.Timestamp);
    }
}
