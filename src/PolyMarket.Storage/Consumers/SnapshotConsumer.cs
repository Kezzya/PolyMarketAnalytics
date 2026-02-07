using MassTransit;
using PolyMarket.Contracts.Messages;
using PolyMarket.Storage.Data;
using PolyMarket.Storage.Entities;

namespace PolyMarket.Storage.Consumers;

public class SnapshotConsumer : IConsumer<MarketSnapshotUpdated>
{
    private readonly AppDbContext _db;
    private readonly ILogger<SnapshotConsumer> _logger;

    public SnapshotConsumer(AppDbContext db, ILogger<SnapshotConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MarketSnapshotUpdated> context)
    {
        var msg = context.Message;

        var market = await _db.Markets.FindAsync(msg.MarketId);
        if (market is null)
        {
            market = new MarketEntity
            {
                Id = msg.MarketId,
                Question = msg.Question
            };
            _db.Markets.Add(market);
        }
        else
        {
            market.Question = msg.Question;
        }

        _db.PriceHistory.Add(new PriceHistoryEntity
        {
            MarketId = msg.MarketId,
            YesPrice = msg.YesPrice,
            NoPrice = msg.NoPrice,
            Volume24h = msg.Volume24h,
            Liquidity = msg.Liquidity,
            Timestamp = msg.Timestamp
        });

        await _db.SaveChangesAsync();
        _logger.LogDebug("Saved snapshot for market {MarketId}", msg.MarketId);
    }
}
