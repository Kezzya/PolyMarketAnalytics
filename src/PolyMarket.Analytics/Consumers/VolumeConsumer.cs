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

        // 1. Volume spike detection
        var volumeAnomaly = _volumeDetector.Detect(msg);
        if (volumeAnomaly is not null)
        {
            _logger.LogWarning("Volume spike: {MarketId}", msg.MarketId);
            await _bus.Publish(volumeAnomaly);
        }

        _volumeDetector.UpdateAverage(msg.MarketId, msg.Volume24h);

        // 2. Near resolution detection
        var nearResolution = _divergenceDetector.DetectNearResolution(msg);
        if (nearResolution is not null)
        {
            _logger.LogWarning("Near resolution: {MarketId} YES={YesPrice}", msg.MarketId, msg.YesPrice);
            await _bus.Publish(nearResolution);
        }

        // 3. YES+NO price sum divergence (should be ~1.0)
        var divergence = _divergenceDetector.DetectPriceSumDivergence(msg);
        if (divergence is not null)
        {
            _logger.LogWarning("Price sum divergence: {MarketId} YES+NO={Sum}",
                msg.MarketId, msg.YesPrice + msg.NoPrice);
            await _bus.Publish(divergence);
        }

        _divergenceDetector.UpdatePrice(msg.MarketId, msg.YesPrice);
        _divergenceDetector.UpdateSnapshot(msg);
    }
}
