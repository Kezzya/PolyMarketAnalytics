using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolyMarket.Alerting.Services;

/// <summary>
/// Resolves conditionId (0x...) → human-readable market question + event slug via Gamma API.
/// Polymarket URL = https://polymarket.com/event/{EVENT_SLUG} (NOT market slug!)
/// </summary>
public class MarketNameResolver
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MarketNameResolver> _logger;
    private readonly string _baseUrl;
    private readonly ConcurrentDictionary<string, MarketInfo> _cache = new();
    private DateTime _lastBulkLoad = DateTime.MinValue;

    public MarketNameResolver(
        IHttpClientFactory httpFactory,
        ILogger<MarketNameResolver> logger,
        IConfiguration config)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _baseUrl = config["Polymarket:GammaApiUrl"] ?? "https://gamma-api.polymarket.com/";
    }

    public async Task<MarketInfo> ResolveAsync(string marketId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(marketId, out var cached))
            return cached;

        // Bulk load if cache is cold or stale
        if (_cache.IsEmpty || DateTime.UtcNow - _lastBulkLoad > TimeSpan.FromMinutes(30))
        {
            await BulkLoadAsync(ct);
            if (_cache.TryGetValue(marketId, out cached))
                return cached;
        }

        // Single lookup fallback — markets endpoint includes events[]
        try
        {
            using var http = CreateClient();
            var response = await http.GetAsync($"markets?condition_id={marketId}", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var markets = JsonSerializer.Deserialize<List<GammaMarketFull>>(json);
                if (markets is { Count: > 0 })
                {
                    var m = markets[0];
                    var eventSlug = m.Events?.FirstOrDefault()?.Slug ?? "";
                    var info = new MarketInfo(m.Question ?? marketId, eventSlug);
                    _cache[marketId] = info;
                    return info;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve market {MarketId}", marketId);
        }

        var shortId = marketId.Length > 10 ? $"{marketId[..6]}...{marketId[^4..]}" : marketId;
        var fallback = new MarketInfo(shortId, "");
        _cache[marketId] = fallback;
        return fallback;
    }

    private async Task BulkLoadAsync(CancellationToken ct)
    {
        try
        {
            // Load from /events endpoint — gives us event slug directly
            _logger.LogInformation("Bulk loading events from Gamma API...");
            using var http = CreateClient();
            var offset = 0;
            var loaded = 0;

            while (offset < 5000)
            {
                var response = await http.GetAsync(
                    $"events?limit=50&offset={offset}&active=true&closed=false", ct);

                if (!response.IsSuccessStatusCode) break;

                var json = await response.Content.ReadAsStringAsync(ct);
                var events = JsonSerializer.Deserialize<List<GammaEvent>>(json);

                if (events is null || events.Count == 0) break;

                foreach (var ev in events)
                {
                    if (ev.Markets is null) continue;

                    foreach (var m in ev.Markets)
                    {
                        if (!string.IsNullOrEmpty(m.ConditionId))
                        {
                            _cache[m.ConditionId] = new MarketInfo(
                                m.Question ?? ev.Title ?? "",
                                ev.Slug ?? "");
                        }
                    }
                }

                loaded += events.Count;
                offset += 50;

                if (events.Count < 50) break;
            }

            _lastBulkLoad = DateTime.UtcNow;
            _logger.LogInformation("Loaded {Count} events ({Markets} markets) into cache",
                loaded, _cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bulk load events");
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpFactory.CreateClient("GammaApi");
        client.BaseAddress = new Uri(_baseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    // Models for /events endpoint
    private class GammaEvent
    {
        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("markets")]
        public List<GammaMarketSlim>? Markets { get; set; }
    }

    private class GammaMarketSlim
    {
        [JsonPropertyName("conditionId")]
        public string ConditionId { get; set; } = "";

        [JsonPropertyName("question")]
        public string? Question { get; set; }
    }

    // Model for /markets endpoint (single lookup, includes events[])
    private class GammaMarketFull
    {
        [JsonPropertyName("conditionId")]
        public string ConditionId { get; set; } = "";

        [JsonPropertyName("question")]
        public string? Question { get; set; }

        [JsonPropertyName("events")]
        public List<GammaEventSlim>? Events { get; set; }
    }

    private class GammaEventSlim
    {
        [JsonPropertyName("slug")]
        public string? Slug { get; set; }
    }

    public record MarketInfo(string Question, string EventSlug)
    {
        public string GetPolymarketUrl()
        {
            if (!string.IsNullOrEmpty(EventSlug))
                return $"https://polymarket.com/event/{EventSlug}";
            return "";
        }
    }
}
