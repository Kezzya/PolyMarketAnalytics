using Microsoft.EntityFrameworkCore;
using PolyMarket.Storage.Data;

namespace PolyMarket.WebApi.Endpoints;

public static class WhalesEndpoints
{
    public static void MapWhalesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/whales").WithTags("Whales");

        group.MapGet("/", async (AppDbContext db, int limit = 20) =>
        {
            var whales = await db.WhaleTrades
                .GroupBy(t => t.TraderAddress)
                .Select(g => new
                {
                    Address = g.Key,
                    TradeCount = g.Count(),
                    TotalVolume = g.Sum(t => t.Size * t.Price),
                    LastTrade = g.Max(t => t.Timestamp)
                })
                .OrderByDescending(w => w.TotalVolume)
                .Take(limit)
                .ToListAsync();

            return Results.Ok(whales);
        });

        group.MapGet("/{address}/history", async (string address, AppDbContext db, int limit = 50) =>
        {
            var trades = await db.WhaleTrades
                .Where(t => t.TraderAddress == address)
                .OrderByDescending(t => t.Timestamp)
                .Take(limit)
                .ToListAsync();

            return trades.Count == 0 ? Results.NotFound() : Results.Ok(trades);
        });
    }
}
