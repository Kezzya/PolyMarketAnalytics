using MassTransit;
using PolyMarket.AutoBet.Clients;
using PolyMarket.AutoBet.Strategy;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.AutoBet.Consumers;

public class AutoBetConsumer : IConsumer<AnomalyDetected>
{
    private readonly AutoBetStrategy _strategy;
    private readonly PolymarketOrderClient _orderClient;
    private readonly IBus _bus;
    private readonly ILogger<AutoBetConsumer> _logger;

    public AutoBetConsumer(
        AutoBetStrategy strategy,
        PolymarketOrderClient orderClient,
        IBus bus,
        ILogger<AutoBetConsumer> logger)
    {
        _strategy = strategy;
        _orderClient = orderClient;
        _bus = bus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AnomalyDetected> context)
    {
        var anomaly = context.Message;

        var decision = _strategy.Evaluate(anomaly);
        if (decision is null)
            return;

        _logger.LogInformation(
            "AUTO BET: {Side} ${Size} @ {Price:F4} on {MarketId} — {Reason}",
            decision.Side, decision.Size, decision.Price, decision.MarketId, decision.Reason);

        // TODO: resolve tokenId from marketId (conditionId)
        // For now, use marketId as tokenId — in production,
        // need to call CLOB API /markets/{conditionId} to get token IDs
        var tokenId = decision.MarketId;

        var result = await _orderClient.PlaceOrderAsync(
            tokenId, decision.Side, decision.Size, decision.Price,
            context.CancellationToken);

        _strategy.RecordBet(decision.MarketId);

        // Publish bet result for Telegram notification
        await _bus.Publish(new BetPlaced(
            MarketId: decision.MarketId,
            TokenId: tokenId,
            Side: decision.Side,
            Size: decision.Size,
            Price: decision.Price,
            TriggerType: anomaly.Type.ToString(),
            TriggerDescription: decision.Reason,
            OrderId: result.OrderId,
            Success: result.Success,
            Error: result.Error,
            Timestamp: DateTime.UtcNow), context.CancellationToken);

        if (result.Success)
        {
            _logger.LogInformation("Bet placed: {OrderId} {Simulated}",
                result.OrderId, result.Simulated ? "(SIMULATED)" : "");
        }
        else
        {
            _logger.LogError("Bet FAILED: {Error}", result.Error);
        }
    }
}
