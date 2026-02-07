using MassTransit;
using PolyMarket.Alerting.Channels;
using PolyMarket.Alerting.Consumers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<TelegramChannel>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AnomalyAlertConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost");
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
