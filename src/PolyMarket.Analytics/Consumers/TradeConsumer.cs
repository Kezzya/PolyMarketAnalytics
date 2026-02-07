using MassTransit;
using PolyMarket.Analytics.Detectors;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Consumers;

public class TradeConsumer : IConsumer<LargeTradeDetected>
{
    private readonly WhaleDetector _detector;
    private readonly IBus _bus;
    private readonly ILogger<TradeConsumer> _logger;

    public TradeConsumer(WhaleDetector detector, IBus bus, ILogger<TradeConsumer> logger)
    {
        _detector = detector;
        _bus = bus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<LargeTradeDetected> context)
    {
        var anomaly = _detector.Detect(context.Message);
        if (anomaly is not null)
        {
            _logger.LogWarning("Whale trade detected: {MarketId} ${Value}",
                anomaly.MarketId, context.Message.Size * context.Message.Price);
            await _bus.Publish(anomaly);
        }
    }
}
