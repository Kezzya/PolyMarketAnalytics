using Microsoft.EntityFrameworkCore;
using PolyMarket.Storage.Data;

namespace PolyMarket.WebApi.Endpoints;

public static class AnomaliesEndpoints
{
    public static void MapAnomaliesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/anomalies").WithTags("Anomalies");

        group.MapGet("/", async (AppDbContext db, string? type = null, decimal? minSeverity = null, int limit = 50) =>
        {
            var query = db.Anomalies.AsQueryable();

            if (!string.IsNullOrEmpty(type))
                query = query.Where(a => a.Type == type);

            if (minSeverity.HasValue)
                query = query.Where(a => a.Severity >= minSeverity.Value);

            var anomalies = await query
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .ToListAsync();

            return Results.Ok(anomalies);
        });

        group.MapGet("/stats", async (AppDbContext db, int hours = 24) =>
        {
            var since = DateTimeOffset.UtcNow.AddHours(-hours);

            var stats = await db.Anomalies
                .Where(a => a.Timestamp >= since)
                .GroupBy(a => a.Type)
                .Select(g => new
                {
                    Type = g.Key,
                    Count = g.Count(),
                    AvgSeverity = g.Average(a => a.Severity),
                    MaxSeverity = g.Max(a => a.Severity)
                })
                .ToListAsync();

            return Results.Ok(new { Period = $"{hours}h", Stats = stats });
        });
    }
}
