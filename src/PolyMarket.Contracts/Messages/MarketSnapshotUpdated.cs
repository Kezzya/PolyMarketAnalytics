namespace PolyMarket.Contracts.Messages;

public record MarketSnapshotUpdated(
    string MarketId,
    string Question,
    decimal YesPrice,
    decimal NoPrice,
    decimal Volume24h,
    decimal Liquidity,
    DateTime Timestamp,
    string? EndDate = null,
    string? Category = null);
