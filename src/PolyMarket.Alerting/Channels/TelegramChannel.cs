using System.Collections.Concurrent;
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

    // Rate limiting: max N alerts per minute
    private readonly int _maxAlertsPerMinute;
    private readonly ConcurrentQueue<DateTime> _sentTimestamps = new();

    // Deduplication: don't send same market+type within cooldown
    private readonly ConcurrentDictionary<string, DateTime> _recentAlerts = new();
    private readonly TimeSpan _deduplicationCooldown = TimeSpan.FromMinutes(5);

    public TelegramChannel(IConfiguration config, ILogger<TelegramChannel> logger, MarketNameResolver resolver)
    {
        _logger = logger;
        _resolver = resolver;
        var token = config["Telegram:BotToken"];
        _chatId = config["Telegram:ChatId"];
        _maxAlertsPerMinute = int.Parse(config["Alerting:MaxAlertsPerMinute"] ?? "10");

        if (!string.IsNullOrEmpty(token))
        {
            _bot = new TelegramBotClient(token);
            _logger.LogInformation("Telegram bot initialized (rate limit: {Max}/min)", _maxAlertsPerMinute);
        }
        else
        {
            _logger.LogWarning("Telegram bot token not configured, alerts will be logged only");
        }
    }

    public async Task SendAlertAsync(AnomalyDetected anomaly, CancellationToken ct = default)
    {
        // Deduplication: skip if same market+type was alerted recently
        var dedupeKey = $"{anomaly.MarketId}:{anomaly.Type}";
        var now = DateTime.UtcNow;
        if (_recentAlerts.TryGetValue(dedupeKey, out var lastSent) && now - lastSent < _deduplicationCooldown)
        {
            _logger.LogDebug("Skipping duplicate alert: {Type} {MarketId} (sent {Ago}s ago)",
                anomaly.Type, anomaly.MarketId, (now - lastSent).TotalSeconds);
            return;
        }

        // Rate limiting: max N per minute
        CleanupOldTimestamps();
        if (_sentTimestamps.Count >= _maxAlertsPerMinute)
        {
            _logger.LogWarning("Rate limit reached ({Max}/min), dropping alert: {Type} {MarketId}",
                _maxAlertsPerMinute, anomaly.Type, anomaly.MarketId);
            return;
        }

        // Resolve market name: 0x... hash → "Will Bitcoin hit $150k?"
        var marketName = await _resolver.ResolveAsync(anomaly.MarketId, ct);

        var emoji = anomaly.Type switch
        {
            AnomalyType.PriceSpike => "\u26a1",
            AnomalyType.VolumeSpike => "\ud83d\udcc8",
            AnomalyType.WhaleTrade => "\ud83d\udc33",
            AnomalyType.MarketDivergence => "\u26a0\ufe0f",
            AnomalyType.NearResolution => "\ud83c\udfaf",
            AnomalyType.OrderBookImbalance => "\ud83d\udcca",
            AnomalyType.SpreadAnomaly => "\ud83d\udcc9",
            AnomalyType.NewsImpact => "\ud83d\udcf0",
            AnomalyType.ArbitrageOpportunity => "\ud83d\udcb0",
            _ => "\ud83d\udd14"
        };

        var severityBar = new string('\u2588', (int)(anomaly.Severity * 10));
        var emptyBar = new string('\u2591', 10 - (int)(anomaly.Severity * 10));

        var polymarketUrl = $"https://polymarket.com/event/{anomaly.MarketId}";

        var message = $"""
            {emoji} <b>{anomaly.Type}</b>

            <b>{EscapeHtml(marketName)}</b>

            {anomaly.Description}

            <b>Severity:</b> [{severityBar}{emptyBar}] {anomaly.Severity:P0}
            <b>Time:</b> {anomaly.Timestamp:yyyy-MM-dd HH:mm:ss} UTC
            <a href="{polymarketUrl}">\ud83d\udd17 Polymarket</a>
            """;

        // Add news URL if present
        if (anomaly.Details.TryGetValue("url", out var urlObj) && urlObj is string url && !string.IsNullOrEmpty(url))
        {
            message += $" | <a href=\"{url}\">\ud83d\udcf0 News</a>";
        }

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

                _logger.LogDebug("Alert sent to Telegram: {Type} {Market}", anomaly.Type, marketName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Telegram alert");
            }
        }
        else
        {
            _logger.LogInformation("ALERT (no Telegram): {Message}", anomaly.Description);
        }

        // Cleanup stale deduplication entries periodically
        if (_recentAlerts.Count > 500)
        {
            var staleKeys = _recentAlerts
                .Where(kv => now - kv.Value > _deduplicationCooldown)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in staleKeys)
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
