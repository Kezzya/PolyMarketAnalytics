using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PolyMarket.Collector.Clients;

public class ClobWebSocketClient : IDisposable
{
    private readonly ILogger<ClobWebSocketClient> _logger;
    private readonly string _wsUrl;
    private ClientWebSocket? _ws;

    public event Func<string, Task>? OnMessageReceived;

    public ClobWebSocketClient(ILogger<ClobWebSocketClient> logger, IConfiguration config)
    {
        _logger = logger;
        _wsUrl = config["Polymarket:ClobWsUrl"] ?? "wss://ws-subscriptions-clob.polymarket.com/ws/market";
    }

    public async Task ConnectAndSubscribeAsync(IEnumerable<string> assetIds, CancellationToken ct)
    {
        _ws = new ClientWebSocket();

        try
        {
            await _ws.ConnectAsync(new Uri(_wsUrl), ct);
            _logger.LogInformation("Connected to CLOB WebSocket");

            foreach (var assetId in assetIds)
            {
                var subscribeMsg = JsonSerializer.Serialize(new
                {
                    type = "market",
                    assets_id = assetId
                });

                var bytes = Encoding.UTF8.GetBytes(subscribeMsg);
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }

            _logger.LogInformation("Subscribed to price updates");
            await ReceiveLoopAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WebSocket connection cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket error");
            throw;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];

        while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await _ws.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogWarning("WebSocket closed by server");
                break;
            }

            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

            if (OnMessageReceived is not null)
                await OnMessageReceived(message);
        }
    }

    public void Dispose()
    {
        _ws?.Dispose();
    }
}
