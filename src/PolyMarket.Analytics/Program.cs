using MassTransit;
using PolyMarket.Analytics.Consumers;
using PolyMarket.Analytics.Detectors;
using PolyMarket.Analytics.Services;

var builder = Host.CreateApplicationBuilder(args);

// Existing detectors
builder.Services.AddSingleton<PriceSpikeDetector>();
builder.Services.AddSingleton<VolumeSpikeDetector>();
builder.Services.AddSingleton<WhaleDetector>();
builder.Services.AddSingleton<MarketDivergenceDetector>();
builder.Services.AddSingleton<OrderBookImbalanceDetector>();
builder.Services.AddSingleton<SpreadDetector>();
builder.Services.AddSingleton<NewsImpactDetector>();

// Crypto arbitrage services
builder.Services.AddSingleton<CryptoMarketCache>();
builder.Services.AddSingleton<CryptoMarketMatcher>();
builder.Services.AddSingleton<FairValueCalculator>();
builder.Services.AddSingleton<CryptoDivergenceDetector>();
builder.Services.AddSingleton<QualityScoreCalculator>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PriceChangedConsumer>();
    x.AddConsumer<TradeConsumer>();
    x.AddConsumer<VolumeConsumer>();
    x.AddConsumer<OrderBookConsumer>();
    x.AddConsumer<NewsConsumer>();

    // Crypto arbitrage consumers
    x.AddConsumer<CryptoMarketCacheConsumer>();
    x.AddConsumer<CryptoPriceConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost");
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
