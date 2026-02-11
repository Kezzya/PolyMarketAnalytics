namespace PolyMarket.Analytics.Services;

/// <summary>
/// Calculates "fair" probability for crypto price markets using
/// a simplified Black-Scholes model (log-normal distribution).
///
/// P(price > target at expiry) = N(d2)
/// where d2 = (ln(S/K) + (μ - σ²/2) * T) / (σ * √T)
///
/// S = current price
/// K = target price
/// σ = annualized volatility
/// T = time to expiry in years
/// μ = drift (assumed 0 for simplicity — crypto has no risk-free rate edge)
/// N() = cumulative normal distribution
/// </summary>
public class FairValueCalculator
{
    /// <summary>
    /// Calculate probability that price will be above target at expiry.
    /// Returns value between 0 and 1.
    /// </summary>
    public FairValueResult Calculate(
        decimal currentPrice,
        decimal targetPrice,
        decimal annualizedVolatility,
        DateTime expiryDate)
    {
        var now = DateTime.UtcNow;
        var timeToExpiry = (expiryDate - now).TotalDays / 365.25;

        // If already expired, it's binary: above or below
        if (timeToExpiry <= 0)
        {
            var prob = currentPrice >= targetPrice ? 0.98m : 0.02m;
            return new FairValueResult(prob, 0, (decimal)timeToExpiry, annualizedVolatility);
        }

        var S = (double)currentPrice;
        var K = (double)targetPrice;
        var sigma = (double)annualizedVolatility;
        var T = timeToExpiry;

        // μ = 0 (no drift assumption for crypto)
        var drift = 0.0;

        // d2 = (ln(S/K) + (μ - σ²/2) * T) / (σ * √T)
        var d2 = (Math.Log(S / K) + (drift - sigma * sigma / 2.0) * T) / (sigma * Math.Sqrt(T));

        // P(S_T > K) = N(d2)
        var probability = NormalCDF(d2);

        // Clamp to [0.01, 0.99] — never 100% certain
        probability = Math.Max(0.01, Math.Min(0.99, probability));

        return new FairValueResult(
            FairProbability: (decimal)probability,
            D2: d2,
            TimeToExpiryYears: (decimal)T,
            Volatility: annualizedVolatility);
    }

    /// <summary>
    /// Calculate probability that price will be below target at expiry.
    /// </summary>
    public FairValueResult CalculateBelow(
        decimal currentPrice,
        decimal targetPrice,
        decimal annualizedVolatility,
        DateTime expiryDate)
    {
        var above = Calculate(currentPrice, targetPrice, annualizedVolatility, expiryDate);
        return above with { FairProbability = 1.0m - above.FairProbability };
    }

    /// <summary>
    /// Cumulative distribution function for standard normal distribution.
    /// Uses rational approximation (Abramowitz and Stegun formula 26.2.17).
    /// </summary>
    private static double NormalCDF(double x)
    {
        // Constants
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var sign = x < 0 ? -1 : 1;
        x = Math.Abs(x) / Math.Sqrt(2);

        var t = 1.0 / (1.0 + p * x);
        var y = 1.0 - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

        return 0.5 * (1.0 + sign * y);
    }
}

public record FairValueResult(
    decimal FairProbability,   // "true" probability according to model
    double D2,                 // d2 statistic (for debugging)
    decimal TimeToExpiryYears, // time remaining
    decimal Volatility);       // volatility used
