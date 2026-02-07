using MassTransit;
using PolyMarket.Alerting.Channels;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Alerting.Consumers;

public class AnomalyAlertConsumer : IConsumer<AnomalyDetected>
{
    private readonly TelegramChannel _telegram;
    private readonly ILogger<AnomalyAlertConsumer> _logger;
    private readonly decimal _minSeverity;

    public AnomalyAlertConsumer(
        TelegramChannel telegram,
        ILogger<AnomalyAlertConsumer> logger,
        IConfiguration config)
    {
        _telegram = telegram;
        _logger = logger;
        _minSeverity = decimal.Parse(config["Alerting:MinSeverity"] ?? "0.3");
    }

    public async Task Consume(ConsumeContext<AnomalyDetected> context)
    {
        var anomaly = context.Message;

        if (anomaly.Severity < _minSeverity)
        {
            _logger.LogDebug("Anomaly {Type} below threshold ({Severity} < {Min})",
                anomaly.Type, anomaly.Severity, _minSeverity);
            return;
        }

        await _telegram.SendAlertAsync(anomaly);
    }
}
