using System.Net;
using System.Text;
using PolyMarket.Alerting.Channels;
using PolyMarket.Alerting.Services;

namespace PolyMarket.Alerting.Workers;

/// <summary>
/// Sends daily paper trading report to Telegram at 21:00 UTC.
/// </summary>
public class DailyReportWorker : BackgroundService
{
    private readonly PaperTradingEngine _paper;
    private readonly TelegramChannel _telegram;
    private readonly ILogger<DailyReportWorker> _logger;

    private static readonly TimeSpan ReportTime = new(21, 0, 0); // 21:00 UTC

    public DailyReportWorker(
        PaperTradingEngine paper,
        TelegramChannel telegram,
        ILogger<DailyReportWorker> logger)
    {
        _paper = paper;
        _telegram = telegram;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DailyReportWorker started, report time: {Time} UTC", ReportTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextReport = now.Date.Add(ReportTime);
            if (nextReport <= now)
                nextReport = nextReport.AddDays(1);

            var delay = nextReport - now;
            _logger.LogDebug("Next report in {Delay}", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var report = _paper.GetDailyReport();
                var msg = FormatReport(report);
                await _telegram.SendRawAsync(msg, stoppingToken);
                _logger.LogInformation("Daily report sent: balance=${Balance:N2}, trades today={Today}",
                    report.Balance, report.TodayTrades.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send daily report");
            }
        }
    }

    private static string FormatReport(DailyReport report)
    {
        var sb = new StringBuilder();

        // Header
        var pnlEmoji = report.TotalPnL >= 0 ? "\ud83d\udfe2" : "\ud83d\udd34";
        sb.AppendLine($"\ud83d\udcca <b>DAILY REPORT — {report.Date:MMM dd}</b>");
        sb.AppendLine();

        // Portfolio
        sb.AppendLine("<b>\ud83d\udcb0 PORTFOLIO:</b>");
        sb.AppendLine($"  Balance: <b>${report.Balance:N2}</b> (started ${report.StartingBalance:N2})");
        sb.AppendLine($"  {pnlEmoji} Total P&amp;L: <b>{(report.TotalPnL >= 0 ? "+" : "")}${report.TotalPnL:N2}</b> ({report.TotalPnLPercent:+0.0%;-0.0%})");
        sb.AppendLine();

        // Today's activity
        sb.AppendLine("<b>\ud83d\udcc5 TODAY:</b>");
        if (report.TodayTrades.Count == 0)
        {
            sb.AppendLine("  No closed trades today");
        }
        else
        {
            sb.AppendLine($"  Trades: {report.TodayTrades.Count} ({report.TodayWins}W / {report.TodayLosses}L)");
            sb.AppendLine($"  P&amp;L: {(report.TodayPnL >= 0 ? "+" : "")}${report.TodayPnL:N2}");

            foreach (var trade in report.TodayTrades.Take(5))
            {
                var icon = trade.IsWin ? "\u2705" : "\u274c";
                sb.AppendLine($"  {icon} {WebUtility.HtmlEncode(Truncate(trade.Question, 40))} → {trade.PnLDollars:+0.00;-0.00}");
            }

            if (report.TodayTrades.Count > 5)
                sb.AppendLine($"  ... and {report.TodayTrades.Count - 5} more");
        }
        sb.AppendLine();

        // All-time stats
        sb.AppendLine("<b>\ud83d\udcc8 ALL-TIME:</b>");
        sb.AppendLine($"  Trades: {report.TotalTrades} ({report.TotalWins}W / {report.TotalLosses}L)");
        sb.AppendLine($"  Win rate: <b>{report.WinRate:P0}</b>");
        if (report.TotalWins > 0)
            sb.AppendLine($"  Avg win: +{report.AvgWinPnL:P1} | Avg loss: {report.AvgLossPnL:P1}");

        // Open positions
        if (report.OpenPositions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"<b>\ud83d\udccd OPEN POSITIONS ({report.OpenPositions.Count}):</b>");
            foreach (var pos in report.OpenPositions)
            {
                sb.AppendLine($"  {pos.Direction} ${pos.Size:F2} @ {pos.EntryPrice:F3} — {WebUtility.HtmlEncode(Truncate(pos.Question, 35))}");
            }
        }

        // Warnings
        if (report.IsPaused)
        {
            sb.AppendLine();
            sb.AppendLine("\u26a0\ufe0f <b>PAUSED</b> — loss streak or drawdown limit hit");
        }

        if (report.LossStreak >= 3)
        {
            sb.AppendLine();
            sb.AppendLine($"\u26a0\ufe0f Loss streak: {report.LossStreak}");
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "\u2026";
}
