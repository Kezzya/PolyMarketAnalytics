using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Alerting.Channels;

public class TelegramChannel
{
    private readonly TelegramBotClient? _bot;
    private readonly string? _chatId;
    private readonly ILogger<TelegramChannel> _logger;

    public TelegramChannel(IConfiguration config, ILogger<TelegramChannel> logger)
    {
        _logger = logger;
        var token = config["Telegram:BotToken"];
        _chatId = config["Telegram:ChatId"];

        if (!string.IsNullOrEmpty(token))
        {
            _bot = new TelegramBotClient(token);
            _logger.LogInformation("Telegram bot initialized");
        }
        else
        {
            _logger.LogWarning("Telegram bot token not configured, alerts will be logged only");
        }
    }

    public async Task SendAlertAsync(AnomalyDetected anomaly, CancellationToken ct = default)
    {
        var emoji = anomaly.Type switch
        {
            AnomalyType.PriceSpike => "\u26a1",
            AnomalyType.VolumeSpike => "\ud83d\udcc8",
            AnomalyType.WhaleTrade => "\ud83d\udc33",
            AnomalyType.MarketDivergence => "\u26a0\ufe0f",
            AnomalyType.NearResolution => "\ud83c\udfaf",
            AnomalyType.ArbitrageOpportunity => "\ud83d\udcb0",
            _ => "\ud83d\udd14"
        };

        var severityBar = new string('\u2588', (int)(anomaly.Severity * 10));
        var emptyBar = new string('\u2591', 10 - (int)(anomaly.Severity * 10));

        var message = $"""
            {emoji} <b>{anomaly.Type}</b>

            {anomaly.Description}

            <b>Severity:</b> [{severityBar}{emptyBar}] {anomaly.Severity:P0}
            <b>Market:</b> <code>{anomaly.MarketId}</code>
            <b>Time:</b> {anomaly.Timestamp:yyyy-MM-dd HH:mm:ss} UTC
            """;

        if (_bot is not null && !string.IsNullOrEmpty(_chatId))
        {
            try
            {
                await _bot.SendTextMessageAsync(_chatId, message, parseMode: ParseMode.Html, cancellationToken: ct);
                _logger.LogDebug("Alert sent to Telegram: {Type} {MarketId}", anomaly.Type, anomaly.MarketId);
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
    }
}
