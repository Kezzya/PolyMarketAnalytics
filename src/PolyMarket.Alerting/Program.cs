using MassTransit;
using PolyMarket.Alerting.Channels;
using PolyMarket.Alerting.Consumers;
using PolyMarket.Alerting.Services;
using PolyMarket.Alerting.Workers;

var builder = Host.CreateApplicationBuilder(args);

// HttpClientFactory for MarketNameResolver (resolves 0x... → market question)
builder.Services.AddHttpClient();
builder.Services.AddSingleton<MarketNameResolver>();
builder.Services.AddSingleton<TelegramChannel>();
builder.Services.AddSingleton<PaperTradingEngine>();
builder.Services.AddHostedService<DailyReportWorker>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AnomalyAlertConsumer>();
    x.AddConsumer<BetAlertConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost");
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
