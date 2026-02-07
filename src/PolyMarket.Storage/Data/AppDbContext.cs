using Microsoft.EntityFrameworkCore;
using PolyMarket.Storage.Entities;

namespace PolyMarket.Storage.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<MarketEntity> Markets => Set<MarketEntity>();
    public DbSet<PriceHistoryEntity> PriceHistory => Set<PriceHistoryEntity>();
    public DbSet<WhaleTradeEntity> WhaleTrades => Set<WhaleTradeEntity>();
    public DbSet<AnomalyEntity> Anomalies => Set<AnomalyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketEntity>(e =>
        {
            e.ToTable("markets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Question).HasColumnName("question").IsRequired();
            e.Property(x => x.Category).HasColumnName("category");
            e.Property(x => x.EndDate).HasColumnName("end_date");
            e.Property(x => x.Active).HasColumnName("active").HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<PriceHistoryEntity>(e =>
        {
            e.ToTable("price_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.MarketId).HasColumnName("market_id");
            e.Property(x => x.YesPrice).HasColumnName("yes_price").HasPrecision(10, 6);
            e.Property(x => x.NoPrice).HasColumnName("no_price").HasPrecision(10, 6);
            e.Property(x => x.Volume24h).HasColumnName("volume_24h").HasPrecision(18, 2);
            e.Property(x => x.Liquidity).HasColumnName("liquidity").HasPrecision(18, 2);
            e.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();

            e.HasOne(x => x.Market).WithMany(m => m.PriceHistory).HasForeignKey(x => x.MarketId);
            e.HasIndex(x => new { x.MarketId, x.Timestamp }).IsDescending(false, true)
                .HasDatabaseName("idx_price_history_market_ts");
        });

        modelBuilder.Entity<WhaleTradeEntity>(e =>
        {
            e.ToTable("whale_trades");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.MarketId).HasColumnName("market_id");
            e.Property(x => x.TraderAddress).HasColumnName("trader_address");
            e.Property(x => x.Side).HasColumnName("side");
            e.Property(x => x.Size).HasColumnName("size").HasPrecision(18, 6);
            e.Property(x => x.Price).HasColumnName("price").HasPrecision(10, 6);
            e.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();

            e.HasOne(x => x.Market).WithMany(m => m.WhaleTrades).HasForeignKey(x => x.MarketId);
            e.HasIndex(x => new { x.MarketId, x.Timestamp }).IsDescending(false, true)
                .HasDatabaseName("idx_whale_trades_market_ts");
        });

        modelBuilder.Entity<AnomalyEntity>(e =>
        {
            e.ToTable("anomalies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.MarketId).HasColumnName("market_id");
            e.Property(x => x.Type).HasColumnName("type").IsRequired();
            e.Property(x => x.Severity).HasColumnName("severity").HasPrecision(3, 2);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Details).HasColumnName("details").HasColumnType("jsonb");
            e.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();

            e.HasOne(x => x.Market).WithMany(m => m.Anomalies).HasForeignKey(x => x.MarketId);
            e.HasIndex(x => new { x.MarketId, x.Timestamp }).IsDescending(false, true)
                .HasDatabaseName("idx_anomalies_market_ts");
        });
    }
}
