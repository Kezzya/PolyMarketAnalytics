namespace PolyMarket.Contracts.Messages;

public record AnomalyDetected(
    AnomalyType Type,
    string MarketId,
    string Description,
    decimal Severity,
    Dictionary<string, object> Details,
    DateTime Timestamp);

public enum AnomalyType
{
    PriceSpike,
    VolumeSpike,
    WhaleTrade,
    MarketDivergence,
    NearResolution,
    OrderBookImbalance,
    SpreadAnomaly,
    NewsImpact,
    CryptoDivergence
}
