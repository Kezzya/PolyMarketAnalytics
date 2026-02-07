using MassTransit;
using PolyMarket.Analytics.Detectors;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Consumers;

public class PriceChangedConsumer : IConsumer<MarketPriceChanged>
{
    private readonly PriceSpikeDetector _detector;
    private readonly IBus _bus;
    private readonly ILogger<PriceChangedConsumer> _logger;

    public PriceChangedConsumer(PriceSpikeDetector detector, IBus bus, ILogger<PriceChangedConsumer> logger)
    {
        _detector = detector;
        _bus = bus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MarketPriceChanged> context)
    {
        var anomaly = _detector.Detect(context.Message);
        if (anomaly is not null)
        {
            _logger.LogWarning("Price spike detected: {MarketId} {Change}%",
                anomaly.MarketId, context.Message.ChangePercent);
            await _bus.Publish(anomaly);
        }
    }
}
