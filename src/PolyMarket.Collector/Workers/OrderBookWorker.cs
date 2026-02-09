using MassTransit;
using PolyMarket.Collector.Clients;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Collector.Workers;

public class OrderBookWorker : BackgroundService
{
    private readonly GammaApiClient _gammaApi;
    private readonly ClobApiClient _clobApi;
    private readonly IBus _bus;
    private readonly ILogger<OrderBookWorker> _logger;
    private readonly TimeSpan _interval;

    public OrderBookWorker(
        GammaApiClient gammaApi,
        ClobApiClient clobApi,
        IBus bus,
        ILogger<OrderBookWorker> logger,
        IConfiguration config)
    {
        _gammaApi = gammaApi;
        _clobApi = clobApi;
        _bus = bus;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(
            int.Parse(config["Polymarket:OrderBookIntervalSeconds"] ?? "30"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderBookWorker started, interval={Interval}s", _interval.TotalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOrderBooksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning order books");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ScanOrderBooksAsync(CancellationToken ct)
    {
        var markets = await _gammaApi.GetAllActiveMarketsAsync(ct);
        var topMarkets = markets
            .Where(m => m.Volume > 5000 && m.Liquidity > 1000)
            .OrderByDescending(m => m.Volume)
            .Take(30)
            .ToList();

        _logger.LogDebug("Scanning order books for {Count} markets", topMarkets.Count);

        foreach (var market in topMarkets)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Get token IDs for this market via CLOB API
                var marketInfo = await _clobApi.GetMarketInfoAsync(market.ConditionId, ct);
                if (marketInfo is null || marketInfo.Tokens.Count == 0)
                    continue;

                // Get order book for YES token
                var yesToken = marketInfo.Tokens.FirstOrDefault(t =>
                    t.Outcome.Equals("Yes", StringComparison.OrdinalIgnoreCase));

                if (yesToken is null)
                    yesToken = marketInfo.Tokens[0];

                var book = await _clobApi.GetOrderBookAsync(yesToken.TokenId, ct);
                if (book is null || (book.BestBid == 0 && book.BestAsk == 0))
                    continue;

                await _bus.Publish(new OrderBookUpdated(
                    MarketId: market.ConditionId,
                    AssetId: yesToken.TokenId,
                    BestBid: book.BestBid,
                    BestAsk: book.BestAsk,
                    Spread: book.Spread,
                    BidDepth: book.BidDepth,
                    AskDepth: book.AskDepth,
                    ImbalanceRatio: book.ImbalanceRatio,
                    Timestamp: DateTime.UtcNow), ct);

                _logger.LogDebug("Order book: {Market} bid={Bid:F4} ask={Ask:F4} spread={Spread:F4} imbalance={Imb:F2}",
                    market.ConditionId[..8], book.BestBid, book.BestAsk, book.Spread, book.ImbalanceRatio);

                await Task.Delay(150, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching order book for {Id}", market.ConditionId);
            }
        }
    }
}
