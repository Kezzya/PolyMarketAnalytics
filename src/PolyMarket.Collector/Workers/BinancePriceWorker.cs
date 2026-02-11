using MassTransit;
using PolyMarket.Collector.Clients;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Collector.Workers;

/// <summary>
/// Connects to Binance WebSocket and publishes CryptoPriceUpdate messages
/// every N seconds with current price + volatility.
/// </summary>
public class BinancePriceWorker : BackgroundService
{
    private readonly BinanceWebSocketClient _binance;
    private readonly IBus _bus;
    private readonly ILogger<BinancePriceWorker> _logger;
    private readonly TimeSpan _publishInterval;

    public BinancePriceWorker(
        BinanceWebSocketClient binance,
        IBus bus,
        ILogger<BinancePriceWorker> logger,
        IConfiguration config)
    {
        _binance = binance;
        _bus = bus;
        _logger = logger;
        _publishInterval = TimeSpan.FromSeconds(
            int.Parse(config["Binance:PublishIntervalSeconds"] ?? "30"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BinancePriceWorker starting, publish interval={Interval}s",
            _publishInterval.TotalSeconds);

        // Start publishing task
        var publishTask = PublishPricesLoopAsync(stoppingToken);

        // Connect to Binance (blocks until disconnected)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _binance.ConnectAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Binance connection failed, retrying in 10s...");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        await publishTask;
    }

    private async Task PublishPricesLoopAsync(CancellationToken ct)
    {
        // Wait for initial prices to come in
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var (symbol, currentPrice) in _binance.CurrentPrices)
                {
                    var price24hAgo = _binance.Prices24hAgo.GetValueOrDefault(symbol, currentPrice);
                    var volatility = _binance.GetVolatility(symbol);

                    var update = new CryptoPriceUpdate(
                        Symbol: symbol,
                        CurrentPrice: currentPrice,
                        Price24hAgo: price24hAgo,
                        Volatility24h: volatility,
                        Timestamp: DateTime.UtcNow);

                    await _bus.Publish(update, ct);
                }

                if (_binance.CurrentPrices.Any())
                {
                    _logger.LogDebug("Published prices: {Prices}",
                        string.Join(", ", _binance.CurrentPrices.Select(
                            kv => $"{kv.Key}=${kv.Value:N2}")));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing crypto prices");
            }

            await Task.Delay(_publishInterval, ct);
        }
    }
}
