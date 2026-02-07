using MassTransit;
using PolyMarket.Contracts.Messages;
using PolyMarket.Storage.Data;
using PolyMarket.Storage.Entities;

namespace PolyMarket.Storage.Consumers;

public class TradeConsumer : IConsumer<LargeTradeDetected>
{
    private readonly AppDbContext _db;
    private readonly ILogger<TradeConsumer> _logger;

    public TradeConsumer(AppDbContext db, ILogger<TradeConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<LargeTradeDetected> context)
    {
        var msg = context.Message;

        _db.WhaleTrades.Add(new WhaleTradeEntity
        {
            MarketId = msg.MarketId,
            TraderAddress = msg.TraderAddress,
            Side = msg.Side,
            Size = msg.Size,
            Price = msg.Price,
            Timestamp = msg.Timestamp
        });

        await _db.SaveChangesAsync();
        _logger.LogDebug("Saved whale trade for market {MarketId}, size {Size}", msg.MarketId, msg.Size);
    }
}
