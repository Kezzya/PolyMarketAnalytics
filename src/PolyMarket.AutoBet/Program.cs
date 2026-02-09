using MassTransit;
using Polly;
using Polly.Extensions.Http;
using PolyMarket.AutoBet.Clients;
using PolyMarket.AutoBet.Consumers;
using PolyMarket.AutoBet.Strategy;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<AutoBetStrategy>();

builder.Services.AddHttpClient<PolymarketOrderClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["AutoBet:ClobApiUrl"] ?? "https://clob.polymarket.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(2, retryAttempt =>
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AutoBetConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost");
        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
