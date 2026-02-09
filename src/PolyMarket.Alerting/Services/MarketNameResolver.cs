using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolyMarket.Alerting.Services;

/// <summary>
/// Resolves conditionId (0x...) → human-readable market question via Gamma API.
/// Caches results in-memory so we don't spam the API.
/// Registered as Singleton, uses IHttpClientFactory for proper HttpClient lifecycle.
/// </summary>
public class MarketNameResolver
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MarketNameResolver> _logger;
    private readonly string _baseUrl;
    private readonly ConcurrentDictionary<string, string> _cache = new();
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

    public async Task<string> ResolveAsync(string marketId, CancellationToken ct = default)
    {
        // Check cache first
        if (_cache.TryGetValue(marketId, out var cached))
            return cached;

        // Bulk load if cache is cold or stale
        if (_cache.IsEmpty || DateTime.UtcNow - _lastBulkLoad > TimeSpan.FromMinutes(30))
        {
            await BulkLoadAsync(ct);
            if (_cache.TryGetValue(marketId, out cached))
                return cached;
        }

        // Single lookup as fallback
        try
        {
            using var http = CreateClient();
            var response = await http.GetAsync($"markets?condition_id={marketId}", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                var markets = JsonSerializer.Deserialize<List<GammaMarketSlim>>(json);
                if (markets is { Count: > 0 })
                {
                    var question = markets[0].Question ?? marketId;
                    _cache[marketId] = question;
                    return question;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve market name for {MarketId}", marketId);
        }

        // Fallback: shortened hash
        var shortId = marketId.Length > 10 ? $"{marketId[..6]}...{marketId[^4..]}" : marketId;
        _cache[marketId] = shortId;
        return shortId;
    }

    private async Task BulkLoadAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Bulk loading market names from Gamma API...");
            using var http = CreateClient();
            var offset = 0;
            var loaded = 0;

            while (offset < 5000)
            {
                var response = await http.GetAsync(
                    $"markets?limit=100&offset={offset}&active=true&closed=false", ct);

                if (!response.IsSuccessStatusCode)
                    break;

                var json = await response.Content.ReadAsStringAsync(ct);
                var markets = JsonSerializer.Deserialize<List<GammaMarketSlim>>(json);

                if (markets is null || markets.Count == 0)
                    break;

                foreach (var m in markets)
                {
                    if (!string.IsNullOrEmpty(m.ConditionId) && !string.IsNullOrEmpty(m.Question))
                        _cache[m.ConditionId] = m.Question;
                }

                loaded += markets.Count;
                offset += 100;

                if (markets.Count < 100)
                    break;
            }

            _lastBulkLoad = DateTime.UtcNow;
            _logger.LogInformation("Loaded {Count} market names into cache", loaded);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to bulk load market names");
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpFactory.CreateClient("GammaApi");
        client.BaseAddress = new Uri(_baseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private class GammaMarketSlim
    {
        [JsonPropertyName("conditionId")]
        public string ConditionId { get; set; } = "";

        [JsonPropertyName("question")]
        public string Question { get; set; } = "";
    }
}
