using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class PriceSpikeDetector
{
    private const decimal SpikeThresholdPercent = 15m;  // 15%+ = serious move

    // Value zone: only trade when YES price is in profitable range
    private const decimal MinYesPrice = 0.08m;   // below 8¢ = too speculative
    private const decimal MaxYesPrice = 0.92m;   // above 92¢ = no upside
    private const decimal MinNoPrice = 0.08m;     // NO below 8¢ = no upside

    public AnomalyDetected? Detect(MarketPriceChanged priceChange)
    {
        if (priceChange.ChangePercent < SpikeThresholdPercent)
            return null;

        var oldP = priceChange.OldPrice;
        var newP = priceChange.NewPrice;
        var isDown = newP < oldP;

        // ═══════════════════════════════════════════════
        // STRATEGY: REVERSAL (panic dump → buy the dip)
        // ═══════════════════════════════════════════════
        //
        // YES price crashed → someone panic-sold → price likely to bounce back
        // Only if YES is now in the "value zone" (8¢ - 70¢)
        // Buy YES cheap, sell after rebound
        //
        // Risk/reward math:
        //   Buy YES at $0.25 after crash from $0.40
        //   If rebounds to $0.35 → profit +40%
        //   If goes to $0 → loss $0.25 per share
        //   Expected bounce: ~50% of the drop

        if (isDown && newP >= MinYesPrice && newP <= 0.70m)
        {
            var dropSize = oldP - newP;
            var expectedBounce = dropSize * 0.5m;           // expect 50% rebound
            var potentialProfit = expectedBounce / newP;     // ROI %
            var risk = newP;                                  // max you can lose per share
            var reward = expectedBounce;

            // Only trade if reward/risk > 0.3 (expect 30%+ ROI)
            if (potentialProfit < 0.20m)
                return null;

            var severity = Math.Min(priceChange.ChangePercent / 20m, 1m);
            var buyPrice = newP;

            var description = $"\ud83d\udcc9 YES crashed: {oldP:F2} \u2192 {newP:F2} (-{priceChange.ChangePercent:F0}%)\n" +
                              $"\ud83d\udcb0 Buy YES at <b>${buyPrice:F2}</b>, target <b>${newP + expectedBounce:F2}</b>\n" +
                              $"\ud83c\udfaf Expected profit: <b>+{potentialProfit:P0}</b>\n" +
                              $"\ud83d\udca1 Signal: <b>BUY YES</b> (reversal play)";

            return new AnomalyDetected(
                Type: AnomalyType.PriceSpike,
                MarketId: priceChange.MarketId,
                Description: description,
                Severity: severity,
                Details: new Dictionary<string, object>
                {
                    ["oldPrice"] = oldP,
                    ["newPrice"] = newP,
                    ["changePercent"] = priceChange.ChangePercent,
                    ["signal"] = "BUY YES",
                    ["strategy"] = "reversal",
                    ["buyPrice"] = buyPrice,
                    ["targetPrice"] = newP + expectedBounce,
                    ["expectedROI"] = potentialProfit,
                    ["question"] = priceChange.Question
                },
                Timestamp: priceChange.Timestamp);
        }

        // ═══════════════════════════════════════════════
        // STRATEGY: MOMENTUM (YES pumping → ride the wave)
        // ═══════════════════════════════════════════════
        //
        // YES price surged → smart money moving in → more upside likely
        // Only if YES is still cheap enough to have room to grow

        if (!isDown && newP >= 0.10m && newP <= 0.60m)
        {
            var riseSize = newP - oldP;
            var roomToGrow = 1.0m - newP;                    // how far to $1.00
            var potentialProfit = roomToGrow / newP;           // max ROI if YES resolves

            // Only trade if there's real upside (50%+ room)
            if (potentialProfit < 0.50m)
                return null;

            var severity = Math.Min(priceChange.ChangePercent / 20m, 1m);

            var description = $"\ud83d\udcc8 YES surging: {oldP:F2} \u2192 {newP:F2} (+{priceChange.ChangePercent:F0}%)\n" +
                              $"\ud83d\udcb0 Buy YES at <b>${newP:F2}</b>, upside to <b>$1.00</b>\n" +
                              $"\ud83c\udfaf Max profit if YES: <b>+{potentialProfit:P0}</b>\n" +
                              $"\ud83d\udca1 Signal: <b>BUY YES</b> (momentum play)";

            return new AnomalyDetected(
                Type: AnomalyType.PriceSpike,
                MarketId: priceChange.MarketId,
                Description: description,
                Severity: severity,
                Details: new Dictionary<string, object>
                {
                    ["oldPrice"] = oldP,
                    ["newPrice"] = newP,
                    ["changePercent"] = priceChange.ChangePercent,
                    ["signal"] = "BUY YES",
                    ["strategy"] = "momentum",
                    ["buyPrice"] = newP,
                    ["expectedROI"] = potentialProfit,
                    ["question"] = priceChange.Question
                },
                Timestamp: priceChange.Timestamp);
        }

        // Everything else → skip (bad risk/reward)
        return null;
    }
}
