using System.Text.Json.Serialization;

namespace PolyMarket.Collector.Models;

public class ClobTradeEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("market")]
    public string Market { get; set; } = "";

    [JsonPropertyName("asset_id")]
    public string AssetId { get; set; } = "";

    [JsonPropertyName("side")]
    public string Side { get; set; } = "";

    [JsonPropertyName("size")]
    public string Size { get; set; } = "0";

    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    [JsonPropertyName("maker_address")]
    public string MakerAddress { get; set; } = "";

    [JsonPropertyName("taker_address")]
    public string TakerAddress { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public long TimestampUnix { get; set; }

    [JsonPropertyName("transaction_hash")]
    public string TransactionHash { get; set; } = "";

    public decimal SizeDecimal => decimal.TryParse(Size, out var v) ? v : 0;
    public decimal PriceDecimal => decimal.TryParse(Price, out var v) ? v : 0;
    public decimal TradeValue => SizeDecimal * PriceDecimal;
    public DateTime TimestampUtc => DateTimeOffset.FromUnixTimeSeconds(TimestampUnix).UtcDateTime;
}

public class ClobTradesResponse
{
    [JsonPropertyName("data")]
    public List<ClobTradeEvent> Data { get; set; } = [];

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}
