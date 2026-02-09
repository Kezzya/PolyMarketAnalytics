using System.Collections.Concurrent;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using PolyMarket.Alerting.Services;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Alerting.Channels;

public class TelegramChannel
{
    private readonly TelegramBotClient? _bot;
    private readonly string? _chatId;
    private readonly ILogger<TelegramChannel> _logger;
    private readonly MarketNameResolver _resolver;

    private readonly int _maxAlertsPerMinute;
    private readonly ConcurrentQueue<DateTime> _sentTimestamps = new();

    private readonly ConcurrentDictionary<string, DateTime> _recentAlerts = new();
    private readonly TimeSpan _deduplicationCooldown;

    public TelegramChannel(IConfiguration config, ILogger<TelegramChannel> logger, MarketNameResolver resolver)
    {
        _logger = logger;
        _resolver = resolver;
        var token = config["Telegram:BotToken"];
        _chatId = config["Telegram:ChatId"];
        var dedupeMinutes = int.Parse(config["Alerting:DeduplicationMinutes"] ?? "15");
        _deduplicationCooldown = TimeSpan.FromMinutes(dedupeMinutes);
        _maxAlertsPerMinute = int.Parse(config["Alerting:MaxAlertsPerMinute"] ?? "10");

        if (!string.IsNullOrEmpty(token))
        {
            _bot = new TelegramBotClient(token);
            _logger.LogInformation("Telegram bot initialized (rate limit: {Max}/min)", _maxAlertsPerMinute);
        }
        else
        {
            _logger.LogWarning("Telegram bot token not configured");
        }
    }

    public async Task SendAlertAsync(AnomalyDetected anomaly, CancellationToken ct = default)
    {
        var dedupeKey = $"{anomaly.MarketId}:{anomaly.Type}";
        var now = DateTime.UtcNow;
        if (_recentAlerts.TryGetValue(dedupeKey, out var lastSent) && now - lastSent < _deduplicationCooldown)
            return;

        CleanupOldTimestamps();
        if (_sentTimestamps.Count >= _maxAlertsPerMinute)
        {
            _logger.LogWarning("Rate limit ({Max}/min), dropping: {Type}", _maxAlertsPerMinute, anomaly.Type);
            return;
        }

        var market = await _resolver.ResolveAsync(anomaly.MarketId, ct);
        var polymarketUrl = market.GetPolymarketUrl();

        var emoji = anomaly.Type switch
        {
            AnomalyType.PriceSpike => "\u26a1",
            AnomalyType.VolumeSpike => "\ud83d\udcc8",
            AnomalyType.WhaleTrade => "\ud83d\udc33",
            AnomalyType.OrderBookImbalance => "\ud83d\udcca",
            AnomalyType.NewsImpact => "\ud83d\udcf0",
            _ => "\ud83d\udd14"
        };

        // Build clean message without leading whitespace
        var sb = new StringBuilder();
        sb.AppendLine($"{emoji} <b>{anomaly.Type}</b>");
        sb.AppendLine();
        sb.AppendLine($"<b>{EscapeHtml(market.Question)}</b>");
        sb.AppendLine();
        sb.AppendLine(anomaly.Description);
        sb.AppendLine();
        sb.AppendLine($"<i>{anomaly.Timestamp:HH:mm:ss} UTC</i>");

        if (!string.IsNullOrEmpty(polymarketUrl))
            sb.AppendLine($"\n<a href=\"{polymarketUrl}\">\ud83d\udd17 Open on Polymarket</a>");

        if (anomaly.Details.TryGetValue("url", out var urlObj) && urlObj is string url && !string.IsNullOrEmpty(url))
            sb.AppendLine($"<a href=\"{url}\">\ud83d\udcf0 News</a>");

        var message = sb.ToString();

        if (_bot is not null && !string.IsNullOrEmpty(_chatId))
        {
            try
            {
                await _bot.SendTextMessageAsync(
                    _chatId, message,
                    parseMode: ParseMode.Html,
                    disableWebPagePreview: true,
                    cancellationToken: ct);

                _sentTimestamps.Enqueue(now);
                _recentAlerts[dedupeKey] = now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Telegram alert");
            }
        }

        if (_recentAlerts.Count > 500)
        {
            foreach (var key in _recentAlerts
                .Where(kv => now - kv.Value > _deduplicationCooldown)
                .Select(kv => kv.Key).ToList())
                _recentAlerts.TryRemove(key, out _);
        }
    }

    private void CleanupOldTimestamps()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-1);
        while (_sentTimestamps.TryPeek(out var ts) && ts < cutoff)
            _sentTimestamps.TryDequeue(out _);
    }

    private static string EscapeHtml(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
