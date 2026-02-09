namespace PolyMarket.Contracts.Messages;

public record BetPlaced(
    string MarketId,
    string TokenId,
    string Side,
    decimal Size,
    decimal Price,
    string TriggerType,
    string TriggerDescription,
    string OrderId,
    bool Success,
    string? Error,
    DateTime Timestamp);

public record PlaceBetCommand(
    string MarketId,
    string TokenId,
    string Side,
    decimal Size,
    decimal Price,
    string TriggerType,
    string TriggerDescription,
    DateTime Timestamp);
