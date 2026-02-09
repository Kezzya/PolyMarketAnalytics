using MassTransit;
using PolyMarket.Analytics.Detectors;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Consumers;

public class NewsConsumer : IConsumer<NewsDetected>
{
    private readonly NewsImpactDetector _detector;
    private readonly IBus _bus;
    private readonly ILogger<NewsConsumer> _logger;

    public NewsConsumer(NewsImpactDetector detector, IBus bus, ILogger<NewsConsumer> logger)
    {
        _detector = detector;
        _bus = bus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<NewsDetected> context)
    {
        var anomaly = _detector.Detect(context.Message);
        if (anomaly is not null)
        {
            _logger.LogWarning("News impact: {MarketId} <- {Source}: {Headline}",
                context.Message.MarketId,
                context.Message.Source,
                context.Message.Headline[..Math.Min(50, context.Message.Headline.Length)]);
            await _bus.Publish(anomaly);
        }
    }
}
