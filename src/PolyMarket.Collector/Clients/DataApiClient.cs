using System.Text.Json;
using PolyMarket.Collector.Models;

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

    public async Task<List<ClobTradeEvent>> GetRecentTradesAsync(
        string conditionId, int limit = 100, CancellationToken ct = default)
    {
        var url = $"trades?market={conditionId}&limit={limit}";
        var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch trades for {ConditionId}: {Status}",
                conditionId, response.StatusCode);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync(ct);

        try
        {
            var trades = JsonSerializer.Deserialize<List<ClobTradeEvent>>(json);
            if (trades is not null)
                return trades;
        }
        catch (JsonException) { }

        try
        {
            var wrapped = JsonSerializer.Deserialize<ClobTradesResponse>(json);
            return wrapped?.Data ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse trades for {ConditionId}", conditionId);
            return [];
        }
    }

    public async Task<List<ClobTradeEvent>> GetLargeTradesAsync(
        string conditionId, decimal minValue = 1000m, CancellationToken ct = default)
    {
        var trades = await GetRecentTradesAsync(conditionId, 200, ct);
        return trades.Where(t => t.TradeValue >= minValue).ToList();
    }

    public async Task<JsonDocument?> GetTopHoldersAsync(string conditionId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"positions?market={conditionId}&sizeThreshold=100", ct);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json);
    }
}
