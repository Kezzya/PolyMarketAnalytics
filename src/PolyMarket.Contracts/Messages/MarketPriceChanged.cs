namespace PolyMarket.Contracts.Messages;

public record MarketPriceChanged(
    string MarketId,
    string Question,
    decimal OldPrice,
    decimal NewPrice,
    decimal ChangePercent,
    DateTime Timestamp);
