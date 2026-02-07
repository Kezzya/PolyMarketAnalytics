using System.Text.Json;
using MassTransit;
using PolyMarket.Collector.Clients;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Collector.Workers;

public class PriceStreamWorker : BackgroundService
{
    private readonly ClobWebSocketClient _wsClient;
    private readonly GammaApiClient _gammaApi;
    private readonly IBus _bus;
    private readonly ILogger<PriceStreamWorker> _logger;

    public PriceStreamWorker(
        ClobWebSocketClient wsClient,
        GammaApiClient gammaApi,
        IBus bus,
        ILogger<PriceStreamWorker> logger)
    {
        _wsClient = wsClient;
        _gammaApi = gammaApi;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PriceStreamWorker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var markets = await _gammaApi.GetAllActiveMarketsAsync(stoppingToken);
                var assetIds = markets
                    .Where(m => !string.IsNullOrEmpty(m.ConditionId))
                    .Select(m => m.ConditionId)
                    .ToList();

                _logger.LogInformation("Subscribing to {Count} market price streams", assetIds.Count);

                _wsClient.OnMessageReceived += async message =>
                {
                    try
                    {
                        await HandleWsMessage(message, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error handling WS message");
                    }
                };

                await _wsClient.ConnectAndSubscribeAsync(assetIds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket stream error, reconnecting...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleWsMessage(string message, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;

        if (root.TryGetProperty("market", out var marketId) &&
            root.TryGetProperty("price", out var priceEl))
        {
            _logger.LogDebug("WS price update: {Market} = {Price}", marketId, priceEl);
        }
    }
}
