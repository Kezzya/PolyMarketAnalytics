using Microsoft.EntityFrameworkCore;
using PolyMarket.Storage.Data;

namespace PolyMarket.WebApi.Endpoints;

public static class MarketsEndpoints
{
    public static void MapMarketsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/markets").WithTags("Markets");

        group.MapGet("/", async (AppDbContext db, int limit = 50, int offset = 0) =>
        {
            var markets = await db.Markets
                .Where(m => m.Active)
                .OrderByDescending(m => m.CreatedAt)
                .Skip(offset)
                .Take(limit)
                .Select(m => new
                {
                    m.Id,
                    m.Question,
                    m.Category,
                    m.EndDate,
                    LatestPrice = m.PriceHistory
                        .OrderByDescending(p => p.Timestamp)
                        .Select(p => new { p.YesPrice, p.NoPrice, p.Volume24h, p.Liquidity })
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Results.Ok(markets);
        });

        group.MapGet("/{id}", async (string id, AppDbContext db) =>
        {
            var market = await db.Markets
                .Include(m => m.PriceHistory.OrderByDescending(p => p.Timestamp).Take(100))
                .FirstOrDefaultAsync(m => m.Id == id);

            return market is null ? Results.NotFound() : Results.Ok(market);
        });

        group.MapGet("/{id}/trades", async (string id, AppDbContext db, int limit = 50) =>
        {
            var trades = await db.WhaleTrades
                .Where(t => t.MarketId == id)
                .OrderByDescending(t => t.Timestamp)
                .Take(limit)
                .ToListAsync();

            return Results.Ok(trades);
        });
    }
}
