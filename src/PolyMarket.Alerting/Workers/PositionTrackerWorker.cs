using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolyMarket.Alerting.Channels;
using PolyMarket.Alerting.Services;

namespace PolyMarket.Alerting.Workers;

/// <summary>
/// Periodically checks open paper positions:
///   1. Fetches current YES/NO price from Gamma API
///   2. Closes positions that have resolved (YES=1.0/0.0)
///   3. Triggers stop-loss / take-profit
///   4. Sends Telegram notification on close
/// </summary>
public class PositionTrackerWorker : BackgroundService
{
    private readonly PaperTradingEngine _paper;
    private readonly TelegramChannel _telegram;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PositionTrackerWorker> _logger;

    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);
    private readonly string _gammaBaseUrl;

    public PositionTrackerWorker(
        PaperTradingEngine paper,
        TelegramChannel telegram,
        IHttpClientFactory httpFactory,
        ILogger<PositionTrackerWorker> logger,
        IConfiguration config)
    {
        _paper = paper;
        _telegram = telegram;
        _httpFactory = httpFactory;
        _logger = logger;
        _gammaBaseUrl = config["Polymarket:GammaApiUrl"] ?? "https://gamma-api.polymarket.com/";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PositionTrackerWorker started, check interval={Interval}",
            _checkInterval);

        // Wait for initial startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckPositions(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking positions");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckPositions(CancellationToken ct)
    {
        var positions = _paper.OpenPositions;
        if (positions.Count == 0)
            return;

        _logger.LogDebug("Checking {Count} open positions", positions.Count);

        using var http = _httpFactory.CreateClient("GammaApi");
        http.BaseAddress = new Uri(_gammaBaseUrl);
        http.Timeout = TimeSpan.FromSeconds(15);

        foreach (var position in positions)
        {
            try
            {
                var marketData = await FetchMarketData(http, position.MarketId, ct);
                if (marketData is null)
                    continue;

                // Check if market is resolved
                if (marketData.Closed)
                {
                    var resolvedYes = marketData.OutcomePrice >= 0.95m; // YES won
                    var wonBet = (position.Direction == "YES" && resolvedYes)
                              || (position.Direction == "NO" && !resolvedYes);

                    var trade = _paper.CloseAtResolution(position.MarketId, wonBet);
                    if (trade is not null)
                    {
                        _logger.LogInformation("RESOLVED: {Outcome} | {Dir} {Question} | P&L=${PnL:+0.00;-0.00}",
                            wonBet ? "WIN" : "LOSS", position.Direction, position.Question, trade.PnLDollars);
                        await SendCloseNotification(trade, ct);
                    }
                    continue;
                }

                // Not resolved — check current price for stop-loss / take-profit
                var currentPrice = position.Direction == "YES"
                    ? marketData.OutcomePrice
                    : 1.0m - marketData.OutcomePrice;

                var trade2 = _paper.CheckAndClose(position.MarketId, currentPrice);
                if (trade2 is not null)
                {
                    _logger.LogInformation("{Reason}: {Dir} {Question} | P&L=${PnL:+0.00;-0.00}",
                        trade2.ExitReason, position.Direction, position.Question, trade2.PnLDollars);
                    await SendCloseNotification(trade2, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check position {MarketId}", position.MarketId);
            }
        }
    }

    private async Task<MarketPriceData?> FetchMarketData(
        HttpClient http, string conditionId, CancellationToken ct)
    {
        try
        {
            var response = await http.GetAsync(
                $"markets?condition_id={conditionId}", ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var markets = JsonSerializer.Deserialize<List<GammaMarketData>>(json);

            if (markets is null || markets.Count == 0)
                return null;

            var m = markets[0];

            // Parse outcomePrices — Gamma API returns JSON string like "[0.35, 0.65]"
            decimal yesPrice = 0.5m;
            if (!string.IsNullOrEmpty(m.OutcomePrices))
            {
                try
                {
                    var prices = JsonSerializer.Deserialize<List<decimal>>(m.OutcomePrices);
                    if (prices is { Count: >= 1 })
                        yesPrice = prices[0];
                }
                catch
                {
                    if (decimal.TryParse(m.OutcomePrices, out var single))
                        yesPrice = single;
                }
            }

            return new MarketPriceData
            {
                OutcomePrice = yesPrice,
                Closed = m.Closed
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task SendCloseNotification(PaperTrade trade, CancellationToken ct)
    {
        var icon = trade.IsWin ? "\u2705" : "\u274c";
        var pnlSign = trade.PnLDollars >= 0 ? "+" : "";

        var sb = new StringBuilder();
        sb.AppendLine($"{icon} <b>POSITION CLOSED</b>");
        sb.AppendLine();
        sb.AppendLine($"\ud83c\udfaf {WebUtility.HtmlEncode(trade.Question)}");
        sb.AppendLine($"  {trade.Direction} | Entry: ${trade.EntryPrice:F3} → Exit: ${trade.ExitPrice:F3}");
        sb.AppendLine($"  Reason: {trade.ExitReason}");
        sb.AppendLine($"  P&amp;L: <b>{pnlSign}${trade.PnLDollars:F2}</b> ({trade.PnLPercent:+0.0%;-0.0%})");
        sb.AppendLine();
        sb.AppendLine($"\ud83d\udcb0 Balance: ${trade.BalanceAfter:N2} | Open: {_paper.OpenPositionCount}");

        try
        {
            await _telegram.SendRawAsync(sb.ToString(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send close notification");
        }
    }

    private class GammaMarketData
    {
        [JsonPropertyName("outcomePrices")]
        public string? OutcomePrices { get; set; }

        [JsonPropertyName("closed")]
        public bool Closed { get; set; }
    }

    private class MarketPriceData
    {
        public decimal OutcomePrice { get; set; }
        public bool Closed { get; set; }
    }
}
