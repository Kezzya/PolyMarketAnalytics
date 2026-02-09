using MassTransit;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Alerting.Consumers;

public class BetAlertConsumer : IConsumer<BetPlaced>
{
    private readonly Channels.TelegramChannel _telegram;
    private readonly ILogger<BetAlertConsumer> _logger;

    public BetAlertConsumer(Channels.TelegramChannel telegram, ILogger<BetAlertConsumer> logger)
    {
        _telegram = telegram;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BetPlaced> context)
    {
        var bet = context.Message;

        var emoji = bet.Success ? "\u2705" : "\u274c";
        var simTag = bet.OrderId.StartsWith("SIM-") ? " [SIMULATED]" : "";

        // Create a special anomaly-like alert for the bet
        var anomaly = new AnomalyDetected(
            Type: AnomalyType.ArbitrageOpportunity, // reuse for bet notifications
            MarketId: bet.MarketId,
            Description: $"{emoji}{simTag} Auto-bet: {bet.Side} ${bet.Size:N2} @ {bet.Price:F4}\n" +
                         $"Trigger: {bet.TriggerType} — {bet.TriggerDescription}\n" +
                         $"Order: {bet.OrderId}" +
                         (bet.Success ? "" : $"\nError: {bet.Error}"),
            Severity: bet.Success ? 0.8m : 1.0m,
            Details: new Dictionary<string, object>
            {
                ["side"] = bet.Side,
                ["size"] = bet.Size,
                ["price"] = bet.Price,
                ["orderId"] = bet.OrderId,
                ["trigger"] = bet.TriggerType,
                ["success"] = bet.Success
            },
            Timestamp: bet.Timestamp);

        await _telegram.SendAlertAsync(anomaly);

        _logger.LogInformation("Bet alert sent: {Side} ${Size} {Success}",
            bet.Side, bet.Size, bet.Success ? "OK" : "FAILED");
    }
}
