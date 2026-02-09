using System.Text.Json.Serialization;

namespace PolyMarket.Collector.Models;

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class GammaMarket
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("conditionId")]
    public string ConditionId { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("closed")]
    public bool Closed { get; set; }

    [JsonPropertyName("volume")]
    public decimal Volume { get; set; }

    [JsonPropertyName("liquidity")]
    public decimal Liquidity { get; set; }

    [JsonPropertyName("outcomePrices")]
    public string? OutcomePrices { get; set; }

    [JsonPropertyName("outcomes")]
    public string? Outcomes { get; set; }
}
