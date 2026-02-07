using System.Text.Json;
using MassTransit;
using PolyMarket.Contracts.Messages;
using PolyMarket.Storage.Data;
using PolyMarket.Storage.Entities;

namespace PolyMarket.Storage.Consumers;

public class AnomalyConsumer : IConsumer<AnomalyDetected>
{
    private readonly AppDbContext _db;
    private readonly ILogger<AnomalyConsumer> _logger;

    public AnomalyConsumer(AppDbContext db, ILogger<AnomalyConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AnomalyDetected> context)
    {
        var msg = context.Message;

        _db.Anomalies.Add(new AnomalyEntity
        {
            MarketId = msg.MarketId,
            Type = msg.Type.ToString(),
            Severity = msg.Severity,
            Description = msg.Description,
            Details = JsonSerializer.Serialize(msg.Details),
            Timestamp = msg.Timestamp
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation("Saved anomaly {Type} for market {MarketId}, severity {Severity}",
            msg.Type, msg.MarketId, msg.Severity);
    }
}
