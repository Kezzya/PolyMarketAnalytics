using System.Collections.Concurrent;
using MassTransit;
using PolyMarket.Analytics.Detectors;
using PolyMarket.Analytics.Services;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Analytics.Consumers;

public class CryptoMarketCacheConsumer : IConsumer<MarketSnapshotUpdated>
{
    private readonly CryptoMarketCache _cache;
    private readonly ILogger<CryptoMarketCacheConsumer> _logger;

    public CryptoMarketCacheConsumer(CryptoMarketCache cache, ILogger<CryptoMarketCacheConsumer> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<MarketSnapshotUpdated> context)
    {
        var snapshot = context.Message;
        var match = _cache.Matcher.TryMatch(snapshot.Question);

        if (match is null)
            return Task.CompletedTask;

        _cache.Markets[snapshot.MarketId] = new CachedCryptoMarket(
            MarketId: snapshot.MarketId,
            Question: snapshot.Question,
            YesPrice: snapshot.YesPrice,
            Volume: snapshot.Volume24h,
            EndDate: snapshot.EndDate,
            Category: snapshot.Category,
            Match: match,
            LastUpdated: snapshot.Timestamp);

        return Task.CompletedTask;
    }
}

public class CryptoPriceConsumer : IConsumer<CryptoPriceUpdate>
{
    private readonly CryptoMarketCache _cache;
    private readonly CryptoDivergenceDetector _detector;
    private readonly QualityScoreCalculator _scorer;
    private readonly IBus _bus;
    private readonly ILogger<CryptoPriceConsumer> _logger;

    private readonly ConcurrentDictionary<string, DateTime> _lastSignal = new();
    private readonly TimeSpan _signalCooldown = TimeSpan.FromMinutes(30);

    public CryptoPriceConsumer(
        CryptoMarketCache cache,
        CryptoDivergenceDetector detector,
        QualityScoreCalculator scorer,
        IBus bus,
        ILogger<CryptoPriceConsumer> logger)
    {
        _cache = cache;
        _detector = detector;
        _scorer = scorer;
        _bus = bus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CryptoPriceUpdate> context)
    {
        var priceUpdate = context.Message;

        var matchingMarkets = _cache.Markets.Values
            .Where(m => m.Match.Symbol == priceUpdate.Symbol)
            .ToList();

        if (matchingMarkets.Count == 0)
            return;

        foreach (var market in matchingMarkets)
        {
            if (_lastSignal.TryGetValue(market.MarketId, out var lastTime)
                && DateTime.UtcNow - lastTime < _signalCooldown)
                continue;

            var anomaly = _detector.Detect(
                marketId: market.MarketId,
                question: market.Question,
                yesPrice: market.YesPrice,
                match: market.Match,
                currentCryptoPrice: priceUpdate.CurrentPrice,
                volatility: priceUpdate.Volatility24h);

            if (anomaly is null)
                continue;

            // ═══════════════════════════════════════
            // Calculate quality score
            // ═══════════════════════════════════════

            // For crypto-arbitrage, the "anomaly signals" are:
            //   1. Edge > 10% (strong divergence)
            //   2. Edge > 20% (very strong)
            //   3. Volume high (> $500k)
            //   4. Volatility reasonable (30-100%)
            //   5. News catalyst (future: check news feed)

            int anomalySignals = 0;
            var absEdge = Math.Abs((decimal)(anomaly.Details.GetValueOrDefault("absEdge", 0m)));
            if (absEdge >= 0.10m) anomalySignals++;
            if (absEdge >= 0.20m) anomalySignals++;
            if (market.Volume >= 500_000) anomalySignals++;
            if (priceUpdate.Volatility24h >= 0.30m && priceUpdate.Volatility24h <= 1.0m) anomalySignals++;
            // News catalyst placeholder — always count crypto price as inherently objective
            anomalySignals++;

            var qualityResult = _scorer.Calculate(
                question: market.Question,
                category: market.Category,
                endDate: market.EndDate,
                volume: market.Volume,
                anomalySignalCount: anomalySignals,
                hasNewsCatalyst: false);

            if (!qualityResult.IsActionable)
            {
                _logger.LogDebug("Quality {Score} blocked for {Question}: {Blocks}",
                    qualityResult.Score, market.Question,
                    string.Join(", ", qualityResult.Blocks));
                continue;
            }

            // Enrich anomaly details with quality data
            var enrichedDetails = new Dictionary<string, object>(anomaly.Details)
            {
                ["qualityScore"] = qualityResult.Score,
                ["marketType"] = qualityResult.Type.ToString(),
                ["hoursToResolution"] = qualityResult.HoursToResolution ?? 0,
                ["scoreReasons"] = string.Join("|", qualityResult.Reasons),
                ["catalyst"] = $"Crypto price divergence: {priceUpdate.Symbol} ${priceUpdate.CurrentPrice:N2}"
            };

            var enrichedAnomaly = anomaly with { Details = enrichedDetails };

            await _bus.Publish(enrichedAnomaly, context.CancellationToken);
            _lastSignal[market.MarketId] = DateTime.UtcNow;

            _logger.LogInformation(
                "\u2705 QUALITY SIGNAL [{Score}]: {Symbol} | {Question} | Edge={Edge:P1}",
                qualityResult.Score, priceUpdate.Symbol, market.Question, absEdge);
        }
    }
}

public class CryptoMarketCache
{
    public CryptoMarketMatcher Matcher { get; } = new();
    public ConcurrentDictionary<string, CachedCryptoMarket> Markets { get; } = new();
}

public record CachedCryptoMarket(
    string MarketId,
    string Question,
    decimal YesPrice,
    decimal Volume,
    string? EndDate,
    string? Category,
    CryptoMarketMatch Match,
    DateTime LastUpdated);
