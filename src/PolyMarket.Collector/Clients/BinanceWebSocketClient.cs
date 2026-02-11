using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PolyMarket.Collector.Clients;

/// <summary>
/// Connects to Binance combined stream for real-time crypto prices.
/// Tracks price history for volatility calculation.
/// No API key needed — public market data.
/// </summary>
public class BinanceWebSocketClient : IDisposable
{
    private readonly ILogger<BinanceWebSocketClient> _logger;
    private ClientWebSocket? _ws;

    // symbol → current price
    public ConcurrentDictionary<string, decimal> CurrentPrices { get; } = new();

    // symbol → price history (for volatility)
    private readonly ConcurrentDictionary<string, List<(DateTime Time, decimal Price)>> _priceHistory = new();

    // symbol → 24h ago price
    public ConcurrentDictionary<string, decimal> Prices24hAgo { get; } = new();

    public event Func<string, decimal, Task>? OnPriceUpdate;

    // Symbols we track — lowercase for Binance API
    public static readonly Dictionary<string, string> SymbolMap = new()
    {
        ["btcusdt"] = "BTC",
        ["ethusdt"] = "ETH",
        ["solusdt"] = "SOL",
        ["dogeusdt"] = "DOGE",
        ["xrpusdt"] = "XRP",
        ["maticusdt"] = "MATIC",
        ["suiusdt"] = "SUI",
    };

    public BinanceWebSocketClient(ILogger<BinanceWebSocketClient> logger)
    {
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        // Binance combined stream: wss://stream.binance.com:9443/stream?streams=btcusdt@ticker/ethusdt@ticker/...
        var streams = string.Join("/", SymbolMap.Keys.Select(s => $"{s}@ticker"));
        var url = $"wss://stream.binance.com:9443/stream?streams={streams}";

        _ws = new ClientWebSocket();

        try
        {
            await _ws.ConnectAsync(new Uri(url), ct);
            _logger.LogInformation("Connected to Binance WebSocket, tracking: {Symbols}",
                string.Join(", ", SymbolMap.Values));

            await ReceiveLoopAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Binance WebSocket cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Binance WebSocket error");
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
                _logger.LogWarning("Binance WebSocket closed by server");
                break;
            }

            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

            try
            {
                await ProcessMessageAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error processing Binance message");
            }
        }
    }

    private async Task ProcessMessageAsync(string message)
    {
        // Binance combined stream format:
        // { "stream": "btcusdt@ticker", "data": { "s": "BTCUSDT", "c": "98450.25", ... } }
        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var data))
            return;

        if (!data.TryGetProperty("s", out var symbolProp))
            return;

        var binanceSymbol = symbolProp.GetString()?.ToLowerInvariant() ?? "";
        if (!SymbolMap.TryGetValue(binanceSymbol, out var symbol))
            return;

        // "c" = last price (close)
        if (!data.TryGetProperty("c", out var priceProp))
            return;

        if (!decimal.TryParse(priceProp.GetString(), out var price) || price <= 0)
            return;

        // Update current price
        CurrentPrices[symbol] = price;

        // Track price history for volatility
        var now = DateTime.UtcNow;
        var history = _priceHistory.GetOrAdd(symbol, _ => new List<(DateTime, decimal)>());

        lock (history)
        {
            history.Add((now, price));

            // Keep last 24h of data, sample every ~5 minutes
            var cutoff = now.AddHours(-25);
            history.RemoveAll(h => h.Time < cutoff);

            // Set 24h ago price (closest sample to 24h ago)
            var target24h = now.AddHours(-24);
            var closest = history
                .Where(h => h.Time <= target24h.AddMinutes(30))
                .OrderByDescending(h => h.Time)
                .FirstOrDefault();

            if (closest != default)
                Prices24hAgo[symbol] = closest.Price;
        }

        if (OnPriceUpdate is not null)
            await OnPriceUpdate(symbol, price);
    }

    /// <summary>
    /// Calculate annualized realized volatility from price history.
    /// Uses 5-minute returns over the last 24 hours.
    /// </summary>
    public decimal GetVolatility(string symbol)
    {
        if (!_priceHistory.TryGetValue(symbol, out var history))
            return 0.60m; // default 60% for crypto

        List<(DateTime Time, decimal Price)> samples;
        lock (history)
        {
            if (history.Count < 10)
                return 0.60m;

            // Sample every ~5 minutes
            samples = history
                .GroupBy(h => new DateTime(h.Time.Year, h.Time.Month, h.Time.Day,
                    h.Time.Hour, h.Time.Minute / 5 * 5, 0))
                .Select(g => g.Last())
                .OrderBy(h => h.Time)
                .ToList();
        }

        if (samples.Count < 10)
            return 0.60m;

        // Calculate log returns
        var returns = new List<double>();
        for (int i = 1; i < samples.Count; i++)
        {
            var logReturn = Math.Log((double)(samples[i].Price / samples[i - 1].Price));
            returns.Add(logReturn);
        }

        // Standard deviation of returns
        var mean = returns.Average();
        var variance = returns.Average(r => Math.Pow(r - mean, 2));
        var stdDev = Math.Sqrt(variance);

        // Annualize: 5-min returns → yearly
        // Periods per year = 365.25 * 24 * 12 = 105,192 (5-minute periods)
        var periodsPerYear = 365.25 * 24 * 12;
        var annualizedVol = stdDev * Math.Sqrt(periodsPerYear);

        return (decimal)Math.Max(0.10, Math.Min(annualizedVol, 3.0)); // clamp 10%-300%
    }

    public void Dispose()
    {
        _ws?.Dispose();
    }
}
