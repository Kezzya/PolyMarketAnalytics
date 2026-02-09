using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Detectors;

public class NewsImpactDetector
{
    private const decimal MinRelevanceForAlert = 0.4m;

    public AnomalyDetected? Detect(NewsDetected news)
    {
        if (news.RelevanceScore < MinRelevanceForAlert)
            return null;

        var severity = Math.Min(news.RelevanceScore, 1m);

        return new AnomalyDetected(
            Type: AnomalyType.NewsImpact,
            MarketId: news.MarketId,
            Description: $"News: \"{news.Headline[..Math.Min(80, news.Headline.Length)]}\" ({news.Source})",
            Severity: severity,
            Details: new Dictionary<string, object>
            {
                ["headline"] = news.Headline,
                ["source"] = news.Source,
                ["url"] = news.Url,
                ["relevanceScore"] = news.RelevanceScore
            },
            Timestamp: news.Timestamp);
    }
}
