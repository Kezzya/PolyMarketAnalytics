using System.Text.Json;
using MassTransit;
using PolyMarket.Collector.Clients;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Collector.Workers;

public class MarketSyncWorker : BackgroundService
{
    private readonly GammaApiClient _gammaApi;
    private readonly IBus _bus;
    private readonly ILogger<MarketSyncWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly decimal _minVolume;
    private readonly decimal _minLiquidity;

    private readonly Dictionary<string, decimal> _lastPrices = new();

    public MarketSyncWorker(
        GammaApiClient gammaApi,
        IBus bus,
        ILogger<MarketSyncWorker> logger,
        IConfiguration config)
    {
        _gammaApi = gammaApi;
        _bus = bus;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(
            int.Parse(config["Polymarket:SyncIntervalSeconds"] ?? "60"));
        _minVolume = decimal.Parse(config["Polymarket:MinVolume"] ?? "10000");
        _minLiquidity = decimal.Parse(config["Polymarket:MinLiquidity"] ?? "500");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MarketSyncWorker started, interval={Interval}s, minVolume=${MinVol}, minLiquidity=${MinLiq}",
            _interval.TotalSeconds, _minVolume, _minLiquidity);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncMarketsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during market sync");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task SyncMarketsAsync(CancellationToken ct)
    {
        var markets = await _gammaApi.GetAllActiveMarketsAsync(ct);

        // Filter out dead markets — no point tracking $0 volume markets
        var alive = markets
            .Where(m => m.Volume >= _minVolume && m.Liquidity >= _minLiquidity)
            .ToList();

        _logger.LogInformation("Syncing {Alive}/{Total} markets (filtered by vol>=${MinVol}, liq>=${MinLiq})",
            alive.Count, markets.Count, _minVolume, _minLiquidity);

        foreach (var market in alive)
        {
            var (yesPrice, noPrice) = ParsePrices(market.OutcomePrices);
            if (yesPrice <= 0) continue;

            var snapshot = new MarketSnapshotUpdated(
                MarketId: market.ConditionId,
                Question: market.Question,
                YesPrice: yesPrice,
                NoPrice: noPrice,
                Volume24h: market.Volume,
                Liquidity: market.Liquidity,
                Timestamp: DateTime.UtcNow,
                EndDate: market.EndDate,
                Category: market.Category);

            await _bus.Publish(snapshot, ct);

            // Only publish price changes for meaningful moves (5%+)
            if (_lastPrices.TryGetValue(market.ConditionId, out var oldPrice) && oldPrice > 0)
            {
                var changePercent = Math.Abs((yesPrice - oldPrice) / oldPrice * 100);
                if (changePercent >= 5m)
                {
                    await _bus.Publish(new MarketPriceChanged(
                        MarketId: market.ConditionId,
                        Question: market.Question,
                        OldPrice: oldPrice,
                        NewPrice: yesPrice,
                        ChangePercent: changePercent,
                        Timestamp: DateTime.UtcNow), ct);
                }
            }

            _lastPrices[market.ConditionId] = yesPrice;
        }
    }

    private static (decimal yesPrice, decimal noPrice) ParsePrices(string? outcomePricesJson)
    {
        if (string.IsNullOrEmpty(outcomePricesJson))
            return (0, 0);

        try
        {
            var prices = JsonSerializer.Deserialize<List<string>>(outcomePricesJson);
            if (prices is null || prices.Count < 2)
                return (0, 0);

            return (decimal.Parse(prices[0]), decimal.Parse(prices[1]));
        }
        catch
        {
            return (0, 0);
        }
    }
}
