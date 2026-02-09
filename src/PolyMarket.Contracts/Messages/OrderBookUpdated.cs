namespace PolyMarket.Contracts.Messages;

public record OrderBookUpdated(
    string MarketId,
    string AssetId,
    decimal BestBid,
    decimal BestAsk,
    decimal Spread,
    decimal BidDepth,
    decimal AskDepth,
    decimal ImbalanceRatio,
    DateTime Timestamp);

public record NewsDetected(
    string MarketId,
    string Headline,
    string Source,
    string Url,
    decimal RelevanceScore,
    DateTime Timestamp);
