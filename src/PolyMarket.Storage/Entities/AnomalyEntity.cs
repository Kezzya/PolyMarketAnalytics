namespace PolyMarket.Storage.Entities;

public class AnomalyEntity
{
    public long Id { get; set; }
    public string MarketId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Severity { get; set; }
    public string? Description { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset Timestamp { get; set; }

    public MarketEntity Market { get; set; } = null!;
}
