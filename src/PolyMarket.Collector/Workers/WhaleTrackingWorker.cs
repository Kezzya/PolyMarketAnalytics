using MassTransit;
using PolyMarket.Collector.Clients;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Collector.Workers;

public class WhaleTrackingWorker : BackgroundService
{
    private readonly GammaApiClient _gammaApi;
    private readonly DataApiClient _dataApi;
    private readonly IBus _bus;
    private readonly ILogger<WhaleTrackingWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly decimal _minTradeValue;

    // Track already-seen trade IDs to avoid duplicates
    private readonly HashSet<string> _seenTradeIds = new();

    public WhaleTrackingWorker(
        GammaApiClient gammaApi,
        DataApiClient dataApi,
        IBus bus,
        ILogger<WhaleTrackingWorker> logger,
        IConfiguration config)
    {
        _gammaApi = gammaApi;
        _dataApi = dataApi;
        _bus = bus;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(
            int.Parse(config["Polymarket:WhaleTrackingIntervalSeconds"] ?? "120"));
        _minTradeValue = decimal.Parse(config["Polymarket:MinWhaleTradeValue"] ?? "1000");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "WhaleTrackingWorker started, interval={Interval}s, minValue=${MinValue}",
            _interval.TotalSeconds, _minTradeValue);

        // Initial delay to let MarketSyncWorker populate markets first
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanForWhaleTradesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning whale trades");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ScanForWhaleTradesAsync(CancellationToken ct)
    {
        // Get top markets by volume — whales trade high-volume markets
        var markets = await _gammaApi.GetAllActiveMarketsAsync(ct);
        var topMarkets = markets
            .Where(m => m.Volume > 10000)
            .OrderByDescending(m => m.Volume)
            .Take(50)
            .ToList();

        _logger.LogInformation("Scanning {Count} top markets for whale trades", topMarkets.Count);

        foreach (var market in topMarkets)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var largeTrades = await _dataApi.GetLargeTradesAsync(
                    market.ConditionId, _minTradeValue, ct);

                foreach (var trade in largeTrades)
                {
                    // Skip already-seen trades
                    var tradeKey = string.IsNullOrEmpty(trade.Id)
                        ? $"{trade.Market}_{trade.MakerAddress}_{trade.TimestampUnix}_{trade.Size}"
                        : trade.Id;

                    if (!_seenTradeIds.Add(tradeKey))
                        continue;

                    var traderAddress = !string.IsNullOrEmpty(trade.TakerAddress)
                        ? trade.TakerAddress
                        : trade.MakerAddress;

                    await _bus.Publish(new LargeTradeDetected(
                        MarketId: market.ConditionId,
                        TraderAddress: string.IsNullOrEmpty(traderAddress) ? "unknown" : traderAddress,
                        Side: trade.Side.ToUpperInvariant(),
                        Size: trade.SizeDecimal,
                        Price: trade.PriceDecimal,
                        Timestamp: trade.TimestampUtc), ct);

                    _logger.LogInformation(
                        "Whale trade found: {Market} {Side} ${Value:N0} by {Trader}",
                        market.Question[..Math.Min(50, market.Question.Length)],
                        trade.Side,
                        trade.TradeValue,
                        traderAddress?[..Math.Min(10, traderAddress?.Length ?? 0)] ?? "?");
                }

                // Rate limiting — don't hammer the API
                await Task.Delay(200, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching trades for market {Id}", market.ConditionId);
            }
        }

        // Prune old trade IDs to prevent memory leak (keep last 10k)
        if (_seenTradeIds.Count > 10000)
        {
            _seenTradeIds.Clear();
            _logger.LogDebug("Cleared seen trade IDs cache");
        }
    }
}
