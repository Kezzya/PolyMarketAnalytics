using System.ServiceModel.Syndication;
using System.Xml;
using MassTransit;
using PolyMarket.Collector.Clients;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Collector.Workers;

public class NewsCollectorWorker : BackgroundService
{
    private readonly GammaApiClient _gammaApi;
    private readonly IBus _bus;
    private readonly ILogger<NewsCollectorWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly HttpClient _http;

    private readonly HashSet<string> _seenLinks = new();
    private List<(string MarketId, string Question, string[] Keywords)> _marketKeywords = [];

    private static readonly string[] NewsSources =
    [
        "https://cointelegraph.com/rss",
        "https://www.coindesk.com/arc/outboundfeeds/rss/",
        "https://cryptonews.com/news/feed/",
        "https://decrypt.co/feed",
        "https://rss.politico.com/politics-news.xml",
        "https://feeds.bbci.co.uk/news/world/rss.xml"
    ];

    public NewsCollectorWorker(
        GammaApiClient gammaApi,
        IBus bus,
        ILogger<NewsCollectorWorker> logger,
        IConfiguration config,
        IHttpClientFactory httpFactory)
    {
        _gammaApi = gammaApi;
        _bus = bus;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(
            int.Parse(config["Polymarket:NewsIntervalSeconds"] ?? "300"));
        _http = httpFactory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NewsCollectorWorker started, interval={Interval}s", _interval.TotalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshMarketKeywordsAsync(stoppingToken);
                await ScanNewsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning news");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RefreshMarketKeywordsAsync(CancellationToken ct)
    {
        var markets = await _gammaApi.GetAllActiveMarketsAsync(ct);

        // Only track top 200 markets by volume — no point matching news against 27k markets
        _marketKeywords = markets
            .Where(m => !string.IsNullOrEmpty(m.Question))
            .OrderByDescending(m => m.Volume)
            .Take(200)
            .Select(m => (
                MarketId: m.ConditionId,
                Question: m.Question,
                Keywords: ExtractKeywords(m.Question)))
            .Where(m => m.Keywords.Length >= 3)
            .ToList();

        _logger.LogInformation("Tracking {Count} top markets for news matching", _marketKeywords.Count);
    }

    private async Task ScanNewsAsync(CancellationToken ct)
    {
        foreach (var source in NewsSources)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var items = await FetchRssFeedAsync(source, ct);

                foreach (var item in items)
                {
                    var link = item.Links.FirstOrDefault()?.Uri?.ToString() ?? "";
                    if (string.IsNullOrEmpty(link) || !_seenLinks.Add(link))
                        continue;

                    var title = item.Title?.Text ?? "";
                    var summary = item.Summary?.Text ?? "";
                    var text = $"{title} {summary}".ToLowerInvariant();

                    // Match against market keywords
                    foreach (var market in _marketKeywords)
                    {
                        var matchCount = market.Keywords.Count(kw => text.Contains(kw));
                        if (matchCount == 0) continue;

                        var relevance = Math.Min((decimal)matchCount / market.Keywords.Length, 1m);
                        if (relevance < 0.5m) continue; // higher threshold to reduce noise

                        await _bus.Publish(new NewsDetected(
                            MarketId: market.MarketId,
                            Headline: title,
                            Source: new Uri(source).Host,
                            Url: link,
                            RelevanceScore: relevance,
                            Timestamp: item.PublishDate.UtcDateTime), ct);

                        _logger.LogInformation(
                            "News matched: [{Source}] \"{Headline}\" -> market \"{Question}\" (relevance={Rel:P0})",
                            new Uri(source).Host,
                            title[..Math.Min(60, title.Length)],
                            market.Question[..Math.Min(50, market.Question.Length)],
                            relevance);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch RSS from {Source}", source);
            }
        }

        // Prune old links
        if (_seenLinks.Count > 5000)
            _seenLinks.Clear();
    }

    private async Task<List<SyndicationItem>> FetchRssFeedAsync(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = XmlReader.Create(stream);
        var feed = SyndicationFeed.Load(reader);

        return feed?.Items?.ToList() ?? [];
    }

    private static string[] ExtractKeywords(string question)
    {
        // Remove common filler words, extract meaningful keywords
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "will", "the", "be", "by", "in", "on", "at", "to", "of", "and", "or",
            "a", "an", "is", "it", "for", "with", "this", "that", "from", "has",
            "have", "was", "are", "been", "before", "after", "during", "end",
            "yes", "no", "above", "below", "more", "than", "less", "over", "under",
            "what", "when", "where", "who", "which", "how", "do", "does", "did",
            "can", "could", "would", "should", "may", "might"
        };

        var words = question
            .ToLowerInvariant()
            .Split([' ', '?', '!', ',', '.', ':', ';', '(', ')', '[', ']', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Distinct()
            .ToArray();

        // Only return if we have meaningful keywords (at least 2)
        return words.Length >= 2 ? words : [];
    }
}
