using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PolyMarket.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "markets",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    question = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: true),
                    end_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_markets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "anomalies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    market_id = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anomalies", x => x.id);
                    table.ForeignKey(
                        name: "FK_anomalies_markets_market_id",
                        column: x => x.market_id,
                        principalTable: "markets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    market_id = table.Column<string>(type: "text", nullable: false),
                    yes_price = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    no_price = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    volume_24h = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    liquidity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_price_history_markets_market_id",
                        column: x => x.market_id,
                        principalTable: "markets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "whale_trades",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    market_id = table.Column<string>(type: "text", nullable: false),
                    trader_address = table.Column<string>(type: "text", nullable: false),
                    side = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whale_trades", x => x.id);
                    table.ForeignKey(
                        name: "FK_whale_trades_markets_market_id",
                        column: x => x.market_id,
                        principalTable: "markets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_anomalies_market_ts",
                table: "anomalies",
                columns: new[] { "market_id", "timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_price_history_market_ts",
                table: "price_history",
                columns: new[] { "market_id", "timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_whale_trades_market_ts",
                table: "whale_trades",
                columns: new[] { "market_id", "timestamp" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anomalies");

            migrationBuilder.DropTable(
                name: "price_history");

            migrationBuilder.DropTable(
                name: "whale_trades");

            migrationBuilder.DropTable(
                name: "markets");
        }
    }
}
