namespace PolyMarket.Analytics.Services;

/// <summary>
/// Calculates quality score (0-100) for market signals.
/// Only signals with score >= 60 are actionable.
///
/// Scoring:
///   Time to resolution: 30 pts max
///   Market type:        25 pts max
///   Market size:        15 pts max
///   Anomaly quality:    30 pts max
/// </summary>
public class QualityScoreCalculator
{
    // === BLOCKED CATEGORIES ===
    private static readonly HashSet<string> BlockedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "awards", "rankings", "ai", "politics"
    };

    // Subjective keywords in question → BLOCK
    private static readonly string[] SubjectiveKeywords =
    [
        "MVP", "DPOY", "best", "Oscar", "Grammy", "Emmy",
        "approval rating", "ranking", "model arena",
        "ROTY", "ROY", "All-Star", "Pro Bowl", "Hall of Fame"
    ];

    // Objective keywords → ALLOW
    private static readonly string[] SportKeywords =
    [
        "win", "beat", "score", "over", "under", "spread",
        "vs", "v.", "match", "game", "fight",
        "Serie A", "Premier League", "NBA", "NFL", "MLB", "NHL",
        "UFC", "Champions League", "La Liga", "Bundesliga"
    ];

    private static readonly string[] PriceBinaryKeywords =
    [
        "above", "below", "reach", "dip", "price",
        "Bitcoin", "BTC", "ETH", "Ethereum", "SOL",
        "S&P", "Nasdaq", "Dow", "gold", "oil",
        "CPI", "jobs report", "unemployment", "Fed", "rate"
    ];

    public QualityScore Calculate(
        string question,
        string? category,
        string? endDate,
        decimal volume,
        int anomalySignalCount,
        bool hasNewsCatalyst)
    {
        int score = 0;
        var reasons = new List<string>();
        var blocks = new List<string>();

        // ════════════════════════════════
        // 0. HARD BLOCKS — skip entirely
        // ════════════════════════════════

        // Block subjective markets
        if (IsSubjective(question, category))
        {
            blocks.Add("Subjective market (awards/rankings/opinions)");
            return new QualityScore(0, MarketType.Blocked, null, reasons, blocks);
        }

        // Block tiny markets
        if (volume < 50_000)
        {
            blocks.Add($"Volume too low: ${volume:N0} (min $50k)");
            return new QualityScore(0, MarketType.Blocked, null, reasons, blocks);
        }

        // ════════════════════════════════
        // 1. TIME TO RESOLUTION (30 pts)
        // ════════════════════════════════

        double? hoursToResolution = null;
        if (DateTime.TryParse(endDate, out var end))
        {
            hoursToResolution = (end - DateTime.UtcNow).TotalHours;

            if (hoursToResolution <= 0)
            {
                blocks.Add("Already expired");
                return new QualityScore(0, MarketType.Blocked, hoursToResolution, reasons, blocks);
            }

            if (hoursToResolution > 7 * 24 && !hasNewsCatalyst) // >1 week without news
            {
                blocks.Add($"Too far from resolution: {hoursToResolution / 24:F0} days (max 7 days without news catalyst)");
                return new QualityScore(0, MarketType.Blocked, hoursToResolution, reasons, blocks);
            }

            if (hoursToResolution <= 24)
            {
                score += 30;
                reasons.Add($"Resolution <24h (+30)");
            }
            else if (hoursToResolution <= 72)
            {
                score += 20;
                reasons.Add($"Resolution <72h (+20)");
            }
            else if (hoursToResolution <= 7 * 24)
            {
                score += 10;
                reasons.Add($"Resolution <7d (+10)");
            }
        }
        else
        {
            // No end date — can't score time, give 5 pts
            score += 5;
            reasons.Add("No end date, partial score (+5)");
        }

        // ════════════════════════════════
        // 2. MARKET TYPE (25 pts)
        // ════════════════════════════════

        var marketType = ClassifyMarket(question, category);

        switch (marketType)
        {
            case MarketType.LiveSports:
                score += 25;
                reasons.Add("Live sports (+25)");
                break;
            case MarketType.PriceBinary:
                score += 20;
                reasons.Add("Price/binary event (+20)");
                break;
            case MarketType.ObjectiveMeasurable:
                score += 15;
                reasons.Add("Objective measurable (+15)");
                break;
            default:
                blocks.Add("Unknown/subjective market type");
                return new QualityScore(score, marketType, hoursToResolution, reasons, blocks);
        }

        // ════════════════════════════════
        // 3. MARKET SIZE (15 pts)
        // ════════════════════════════════

        if (volume >= 1_000_000)
        {
            score += 15;
            reasons.Add($"Volume ${volume:N0} >$1M (+15)");
        }
        else if (volume >= 500_000)
        {
            score += 10;
            reasons.Add($"Volume ${volume:N0} >$500k (+10)");
        }
        else if (volume >= 100_000)
        {
            score += 5;
            reasons.Add($"Volume ${volume:N0} >$100k (+5)");
        }
        else
        {
            blocks.Add($"Volume ${volume:N0} <$100k — too small");
            return new QualityScore(score, marketType, hoursToResolution, reasons, blocks);
        }

        // ════════════════════════════════
        // 4. ANOMALY QUALITY (30 pts)
        // ════════════════════════════════

        // anomalySignalCount = how many of these are true:
        //   volume_spike > 3x, imbalance > 0.7, spread widened > 2x,
        //   depth dropped > 50%, news catalyst present

        if (anomalySignalCount >= 3)
        {
            score += 30;
            reasons.Add($"Anomaly signals: {anomalySignalCount}/5 (+30)");
        }
        else if (anomalySignalCount >= 2)
        {
            score += 15;
            reasons.Add($"Anomaly signals: {anomalySignalCount}/5 (+15)");
        }
        else
        {
            blocks.Add($"Only {anomalySignalCount} anomaly signals (need >=2)");
            return new QualityScore(score, marketType, hoursToResolution, reasons, blocks);
        }

        return new QualityScore(score, marketType, hoursToResolution, reasons, blocks);
    }

    private static bool IsSubjective(string question, string? category)
    {
        if (category is not null && BlockedCategories.Contains(category))
            return true;

        return SubjectiveKeywords.Any(kw =>
            question.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private static MarketType ClassifyMarket(string question, string? category)
    {
        // Sports
        if (category?.Equals("sports", StringComparison.OrdinalIgnoreCase) == true
            || SportKeywords.Any(kw => question.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            return MarketType.LiveSports;

        // Price / crypto / economic data
        if (PriceBinaryKeywords.Any(kw => question.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            return MarketType.PriceBinary;

        // Generic binary "Will X happen?"
        if (question.StartsWith("Will ", StringComparison.OrdinalIgnoreCase))
            return MarketType.ObjectiveMeasurable;

        return MarketType.Unknown;
    }
}

public enum MarketType
{
    Unknown,
    Blocked,
    LiveSports,
    PriceBinary,
    ObjectiveMeasurable
}

public record QualityScore(
    int Score,
    MarketType Type,
    double? HoursToResolution,
    List<string> Reasons,
    List<string> Blocks)
{
    public bool IsActionable => Score >= 60 && Blocks.Count == 0;
}
