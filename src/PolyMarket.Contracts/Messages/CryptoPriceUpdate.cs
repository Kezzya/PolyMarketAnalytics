namespace PolyMarket.Contracts.Messages;

/// <summary>
/// Real-time crypto price from Binance.
/// Published by BinancePriceWorker, consumed by CryptoDivergenceConsumer.
/// </summary>
public record CryptoPriceUpdate(
    string Symbol,          // "BTC", "ETH", "SOL"
    decimal CurrentPrice,   // e.g. 98450.25
    decimal Price24hAgo,    // for calculating 24h volatility
    decimal Volatility24h,  // realized volatility (annualized)
    DateTime Timestamp);
