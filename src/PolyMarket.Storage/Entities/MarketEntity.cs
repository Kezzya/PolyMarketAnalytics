namespace PolyMarket.Storage.Entities;

public class MarketEntity
{
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string? Category { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<PriceHistoryEntity> PriceHistory { get; set; } = [];
    public ICollection<WhaleTradeEntity> WhaleTrades { get; set; } = [];
    public ICollection<AnomalyEntity> Anomalies { get; set; } = [];
}
