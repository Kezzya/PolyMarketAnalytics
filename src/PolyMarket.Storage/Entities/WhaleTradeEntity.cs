namespace PolyMarket.Storage.Entities;

public class WhaleTradeEntity
{
    public long Id { get; set; }
    public string MarketId { get; set; } = string.Empty;
    public string TraderAddress { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal Size { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset Timestamp { get; set; }

    public MarketEntity Market { get; set; } = null!;
}
