using MassTransit;
using PolyMarket.Analytics.Detectors;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Consumers;

public class VolumeConsumer : IConsumer<MarketSnapshotUpdated>
{
    private readonly VolumeSpikeDetector _volumeDetector;
    private readonly MarketDivergenceDetector _divergenceDetector;
    private readonly IBus _bus;
    private readonly ILogger<VolumeConsumer> _logger;

    public VolumeConsumer(
        VolumeSpikeDetector volumeDetector,
        MarketDivergenceDetector divergenceDetector,
        IBus bus,
        ILogger<VolumeConsumer> logger)
    {
        _volumeDetector = volumeDetector;
        _divergenceDetector = divergenceDetector;
        _bus = bus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MarketSnapshotUpdated> context)
    {
        var msg = context.Message;

        var volumeAnomaly = _volumeDetector.Detect(msg);
        if (volumeAnomaly is not null)
        {
            _logger.LogWarning("Volume spike: {MarketId}", msg.MarketId);
            await _bus.Publish(volumeAnomaly);
        }

        _volumeDetector.UpdateAverage(msg.MarketId, msg.Volume24h);

        var nearResolution = _divergenceDetector.DetectNearResolution(msg);
        if (nearResolution is not null)
        {
            _logger.LogWarning("Near resolution: {MarketId} YES={YesPrice}", msg.MarketId, msg.YesPrice);
            await _bus.Publish(nearResolution);
        }

        _divergenceDetector.UpdatePrice(msg.MarketId, msg.YesPrice);
    }
}
