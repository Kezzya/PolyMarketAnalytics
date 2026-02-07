using System.Text.Json;
using PolyMarket.Collector.Models;

namespace PolyMarket.Collector.Clients;

public class GammaApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GammaApiClient> _logger;

    public GammaApiClient(HttpClient http, ILogger<GammaApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<GammaMarket>> GetMarketsAsync(
        int limit = 100, int offset = 0, bool activeOnly = true, CancellationToken ct = default)
    {
        var url = $"markets?limit={limit}&offset={offset}&active={activeOnly.ToString().ToLower()}&closed=false";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var markets = JsonSerializer.Deserialize<List<GammaMarket>>(json) ?? [];

        _logger.LogDebug("Fetched {Count} markets from Gamma API (offset={Offset})", markets.Count, offset);
        return markets;
    }

    public async Task<List<GammaMarket>> GetAllActiveMarketsAsync(CancellationToken ct = default)
    {
        var allMarkets = new List<GammaMarket>();
        var offset = 0;
        const int limit = 100;

        while (true)
        {
            var batch = await GetMarketsAsync(limit, offset, true, ct);
            if (batch.Count == 0)
                break;

            allMarkets.AddRange(batch);
            offset += limit;

            if (batch.Count < limit)
                break;
        }

        _logger.LogInformation("Fetched total {Count} active markets", allMarkets.Count);
        return allMarkets;
    }

    public async Task<GammaMarket?> GetMarketAsync(string slug, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"markets/{slug}", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GammaMarket>(json);
    }
}
