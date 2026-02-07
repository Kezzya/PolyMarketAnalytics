using System.Text.Json;

namespace PolyMarket.Collector.Clients;

public class DataApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<DataApiClient> _logger;

    public DataApiClient(HttpClient http, ILogger<DataApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<JsonDocument?> GetMarketTradesAsync(string conditionId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"trades?market={conditionId}", ct);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json);
    }

    public async Task<JsonDocument?> GetTopHoldersAsync(string conditionId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"positions?market={conditionId}&sizeThreshold=100", ct);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json);
    }
}
