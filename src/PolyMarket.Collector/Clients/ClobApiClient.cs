using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolyMarket.Collector.Clients;

public class ClobApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ClobApiClient> _logger;

    public ClobApiClient(HttpClient http, ILogger<ClobApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<OrderBookResponse?> GetOrderBookAsync(
        string tokenId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"book?token_id={tokenId}", ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch order book for {TokenId}: {Status}",
                tokenId, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);

        try
        {
            return JsonSerializer.Deserialize<OrderBookResponse>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse order book for {TokenId}", tokenId);
            return null;
        }
    }

    public async Task<MarketInfo?> GetMarketInfoAsync(
        string conditionId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"markets/{conditionId}", ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);

        try
        {
            return JsonSerializer.Deserialize<MarketInfo>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public class OrderBookResponse
{
    [JsonPropertyName("market")]
    public string Market { get; set; } = "";

    [JsonPropertyName("asset_id")]
    public string AssetId { get; set; } = "";

    [JsonPropertyName("bids")]
    public List<OrderBookLevel> Bids { get; set; } = [];

    [JsonPropertyName("asks")]
    public List<OrderBookLevel> Asks { get; set; } = [];

    public decimal BestBid => Bids.Count > 0 ? Bids[0].PriceDecimal : 0;
    public decimal BestAsk => Asks.Count > 0 ? Asks[0].PriceDecimal : 0;
    public decimal Spread => BestAsk > 0 && BestBid > 0 ? BestAsk - BestBid : 0;
    public decimal MidPrice => BestAsk > 0 && BestBid > 0 ? (BestAsk + BestBid) / 2 : 0;

    public decimal BidDepth => Bids.Sum(b => b.SizeDecimal * b.PriceDecimal);
    public decimal AskDepth => Asks.Sum(a => a.SizeDecimal * a.PriceDecimal);

    public decimal ImbalanceRatio
    {
        get
        {
            var total = BidDepth + AskDepth;
            return total > 0 ? (BidDepth - AskDepth) / total : 0;
        }
    }
}

public class OrderBookLevel
{
    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    [JsonPropertyName("size")]
    public string Size { get; set; } = "0";

    public decimal PriceDecimal => decimal.TryParse(Price, out var v) ? v : 0;
    public decimal SizeDecimal => decimal.TryParse(Size, out var v) ? v : 0;
}

public class MarketInfo
{
    [JsonPropertyName("condition_id")]
    public string ConditionId { get; set; } = "";

    [JsonPropertyName("tokens")]
    public List<TokenInfo> Tokens { get; set; } = [];
}

public class TokenInfo
{
    [JsonPropertyName("token_id")]
    public string TokenId { get; set; } = "";

    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "";
}
