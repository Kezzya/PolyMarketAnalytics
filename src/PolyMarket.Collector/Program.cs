using MassTransit;
using Polly;
using Polly.Extensions.Http;
using PolyMarket.Collector.Clients;
using PolyMarket.Collector.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<GammaApiClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Polymarket:GammaApiUrl"] ?? "https://gamma-api.polymarket.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, retryAttempt =>
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

builder.Services.AddHttpClient<DataApiClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Polymarket:DataApiUrl"] ?? "https://data-api.polymarket.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, retryAttempt =>
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

builder.Services.AddHttpClient<ClobApiClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Polymarket:ClobApiUrl"] ?? "https://clob.polymarket.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, retryAttempt =>
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

builder.Services.AddSingleton<ClobWebSocketClient>();
builder.Services.AddSingleton<BinanceWebSocketClient>();

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost");
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddHostedService<MarketSyncWorker>();
builder.Services.AddHostedService<PriceStreamWorker>();
builder.Services.AddHostedService<WhaleTrackingWorker>();
builder.Services.AddHostedService<OrderBookWorker>();
builder.Services.AddHostedService<NewsCollectorWorker>();
builder.Services.AddHostedService<BinancePriceWorker>();

var host = builder.Build();
host.Run();
