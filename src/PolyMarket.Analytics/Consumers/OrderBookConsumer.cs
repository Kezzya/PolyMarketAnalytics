using MassTransit;
using PolyMarket.Analytics.Detectors;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Consumers;

public class OrderBookConsumer : IConsumer<OrderBookUpdated>
{
    private readonly OrderBookImbalanceDetector _imbalanceDetector;
    private readonly SpreadDetector _spreadDetector;
    private readonly IBus _bus;
    private readonly ILogger<OrderBookConsumer> _logger;

    public OrderBookConsumer(
        OrderBookImbalanceDetector imbalanceDetector,
        SpreadDetector spreadDetector,
        IBus bus,
        ILogger<OrderBookConsumer> logger)
    {
        _imbalanceDetector = imbalanceDetector;
        _spreadDetector = spreadDetector;
        _bus = bus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderBookUpdated> context)
    {
        var msg = context.Message;

        // 1. Order book imbalance
        var imbalance = _imbalanceDetector.Detect(msg);
        if (imbalance is not null)
        {
            _logger.LogWarning("Order book imbalance: {MarketId} ratio={Ratio:F2}",
                msg.MarketId, msg.ImbalanceRatio);
            await _bus.Publish(imbalance);
        }
        _imbalanceDetector.UpdateAverage(msg.MarketId, msg.ImbalanceRatio);

        // 2. Spread anomaly
        var spread = _spreadDetector.Detect(msg);
        if (spread is not null)
        {
            _logger.LogWarning("Spread anomaly: {MarketId} spread={Spread:F4}",
                msg.MarketId, msg.Spread);
            await _bus.Publish(spread);
        }
        _spreadDetector.UpdateAverage(msg.MarketId, msg.Spread);
    }
}
