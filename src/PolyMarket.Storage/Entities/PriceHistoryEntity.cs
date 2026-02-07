namespace PolyMarket.Storage.Entities;

public class PriceHistoryEntity
{
    public long Id { get; set; }
    public string MarketId { get; set; } = string.Empty;
    public decimal YesPrice { get; set; }
    public decimal NoPrice { get; set; }
    public decimal Volume24h { get; set; }
    public decimal Liquidity { get; set; }
    public DateTimeOffset Timestamp { get; set; }

    public MarketEntity Market { get; set; } = null!;
}
