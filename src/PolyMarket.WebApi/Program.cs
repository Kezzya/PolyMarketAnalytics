using Microsoft.EntityFrameworkCore;
using PolyMarket.Storage.Data;
using PolyMarket.WebApi.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapMarketsEndpoints();
app.MapAnomaliesEndpoints();
app.MapWhalesEndpoints();

app.Run();
