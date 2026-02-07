namespace PolyMarket.Contracts.Messages;

public record MarketVolumeSpike(
    string MarketId,
    decimal NormalVolume24h,
    decimal CurrentVolume1h,
    decimal SpikeMultiplier,
    DateTime Timestamp);
