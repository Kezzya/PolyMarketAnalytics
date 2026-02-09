using MassTransit;
using PolyMarket.Analytics.Consumers;
using PolyMarket.Analytics.Detectors;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<PriceSpikeDetector>();
builder.Services.AddSingleton<VolumeSpikeDetector>();
builder.Services.AddSingleton<WhaleDetector>();
builder.Services.AddSingleton<MarketDivergenceDetector>();
builder.Services.AddSingleton<OrderBookImbalanceDetector>();
builder.Services.AddSingleton<SpreadDetector>();
builder.Services.AddSingleton<NewsImpactDetector>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PriceChangedConsumer>();
    x.AddConsumer<TradeConsumer>();
    x.AddConsumer<VolumeConsumer>();
    x.AddConsumer<OrderBookConsumer>();
    x.AddConsumer<NewsConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost");
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
