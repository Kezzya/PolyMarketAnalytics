namespace PolyMarket.Contracts.Messages;

public record LargeTradeDetected(
    string MarketId,
    string TraderAddress,
    string Side,
    decimal Size,
    decimal Price,
    DateTime Timestamp);
