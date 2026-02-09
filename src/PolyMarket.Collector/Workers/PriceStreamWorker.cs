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

    private readonly Dictionary<string, decimal> _lastPrices = new();

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
                _logger.LogError(ex, "WebSocket stream error, reconnecting in 5s...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleWsMessage(string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message) || message[0] != '{' && message[0] != '[')
            return; // skip non-JSON messages (heartbeat, ping, etc.)

        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;

        if (root.TryGetProperty("market", out var marketEl) &&
            root.TryGetProperty("event_type", out var eventTypeEl))
        {
            var marketId = marketEl.GetString() ?? "";
            var eventType = eventTypeEl.GetString() ?? "";

            switch (eventType)
            {
                case "price_change":
                    await HandlePriceChange(root, marketId, ct);
                    break;
                case "trade":
                    await HandleTrade(root, marketId, ct);
                    break;
                default:
                    if (root.TryGetProperty("price", out _))
                        await HandlePriceChange(root, marketId, ct);
                    break;
            }
        }
        else if (root.TryGetProperty("market", out var mktEl))
        {
            var marketId = mktEl.GetString() ?? "";

            if (root.TryGetProperty("price", out _))
                await HandlePriceChange(root, marketId, ct);

            if (root.TryGetProperty("size", out _) && root.TryGetProperty("trader", out _))
                await HandleTrade(root, marketId, ct);
        }
    }

    private async Task HandlePriceChange(JsonElement root, string marketId, CancellationToken ct)
    {
        decimal newPrice = 0;

        if (root.TryGetProperty("price", out var priceEl))
        {
            if (priceEl.ValueKind == JsonValueKind.String)
                decimal.TryParse(priceEl.GetString(), out newPrice);
            else if (priceEl.ValueKind == JsonValueKind.Number)
                newPrice = priceEl.GetDecimal();
        }

        if (newPrice == 0 && root.TryGetProperty("outcome_prices", out var pricesEl)
            && pricesEl.ValueKind == JsonValueKind.Array && pricesEl.GetArrayLength() >= 1)
        {
            var first = pricesEl[0];
            if (first.ValueKind == JsonValueKind.String)
                decimal.TryParse(first.GetString(), out newPrice);
            else if (first.ValueKind == JsonValueKind.Number)
                newPrice = first.GetDecimal();
        }

        if (newPrice <= 0 || string.IsNullOrEmpty(marketId))
            return;

        if (_lastPrices.TryGetValue(marketId, out var oldPrice) && oldPrice > 0)
        {
            var changePercent = Math.Abs((newPrice - oldPrice) / oldPrice * 100);

            if (changePercent >= 5m) // only publish significant moves
            {
                var priceChanged = new MarketPriceChanged(
                    MarketId: marketId,
                    Question: "",
                    OldPrice: oldPrice,
                    NewPrice: newPrice,
                    ChangePercent: changePercent,
                    Timestamp: DateTime.UtcNow);

                await _bus.Publish(priceChanged, ct);
                _logger.LogInformation("WS price change: {MarketId} {Old:F4} -> {New:F4} ({Change:F1}%)",
                    marketId, oldPrice, newPrice, changePercent);
            }
        }

        _lastPrices[marketId] = newPrice;
    }

    private async Task HandleTrade(JsonElement root, string marketId, CancellationToken ct)
    {
        decimal size = 0, price = 0;
        string trader = "", side = "BUY";

        if (root.TryGetProperty("size", out var sizeEl))
        {
            if (sizeEl.ValueKind == JsonValueKind.String)
                decimal.TryParse(sizeEl.GetString(), out size);
            else if (sizeEl.ValueKind == JsonValueKind.Number)
                size = sizeEl.GetDecimal();
        }

        if (root.TryGetProperty("price", out var priceEl))
        {
            if (priceEl.ValueKind == JsonValueKind.String)
                decimal.TryParse(priceEl.GetString(), out price);
            else if (priceEl.ValueKind == JsonValueKind.Number)
                price = priceEl.GetDecimal();
        }

        if (root.TryGetProperty("trader", out var traderEl))
            trader = traderEl.GetString() ?? "";
        else if (root.TryGetProperty("maker_address", out var makerEl))
            trader = makerEl.GetString() ?? "";
        else if (root.TryGetProperty("taker_address", out var takerEl))
            trader = takerEl.GetString() ?? "";

        if (root.TryGetProperty("side", out var sideEl))
            side = sideEl.GetString()?.ToUpperInvariant() ?? "BUY";

        if (size <= 0 || string.IsNullOrEmpty(marketId))
            return;

        var tradeValue = size * price;

        // Publish trades > $500 — WhaleDetector will filter for > $10k
        if (tradeValue >= 500m)
        {
            var trade = new LargeTradeDetected(
                MarketId: marketId,
                TraderAddress: string.IsNullOrEmpty(trader) ? "unknown" : trader,
                Side: side,
                Size: size,
                Price: price,
                Timestamp: DateTime.UtcNow);

            await _bus.Publish(trade, ct);
            _logger.LogInformation("WS trade: {MarketId} {Side} size={Size} price={Price:F4} value=${Value:N0}",
                marketId, side, size, price, tradeValue);
        }
    }
}
