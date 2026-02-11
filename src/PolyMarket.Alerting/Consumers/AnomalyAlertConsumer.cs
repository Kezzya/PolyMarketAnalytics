using System.Net;
using System.Text;
using MassTransit;
using PolyMarket.Alerting.Channels;
using PolyMarket.Alerting.Services;
using PolyMarket.Contracts.Messages;

namespace PolyMarket.Alerting.Consumers;

public class AnomalyAlertConsumer : IConsumer<AnomalyDetected>
{
    private readonly TelegramChannel _telegram;
    private readonly PaperTradingEngine _paper;
    private readonly MarketNameResolver _resolver;
    private readonly ILogger<AnomalyAlertConsumer> _logger;

    // Rate limiting: max 5 signals per day, 1 per 30 min
    private static int _todaySignals = 0;
    private static DateTime _todayDate = DateTime.UtcNow.Date;
    private static DateTime _lastSignalTime = DateTime.MinValue;
    private const int MaxSignalsPerDay = 5;
    private static readonly TimeSpan MinSignalInterval = TimeSpan.FromMinutes(30);

    public AnomalyAlertConsumer(
        TelegramChannel telegram,
        PaperTradingEngine paper,
        MarketNameResolver resolver,
        ILogger<AnomalyAlertConsumer> logger)
    {
        _telegram = telegram;
        _paper = paper;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AnomalyDetected> context)
    {
        var anomaly = context.Message;

        // Must have signal + strategy from detectors
        if (!anomaly.Details.TryGetValue("signal", out var signalObj))
            return;

        var signal = signalObj?.ToString() ?? "";
        if (signal != "BUY YES" && signal != "BUY NO")
            return;

        // Must have quality score data
        var qualityScore = GetInt(anomaly.Details, "qualityScore");
        if (qualityScore < 60)
        {
            _logger.LogDebug("Quality {Score} < 60, skipping {MarketId}", qualityScore, anomaly.MarketId);
            return;
        }

        // Rate limiting
        ResetDayCounterIfNeeded();
        if (_todaySignals >= MaxSignalsPerDay)
        {
            _logger.LogWarning("Daily limit ({Max}/day), dropping {Type}", MaxSignalsPerDay, anomaly.Type);
            return;
        }
        if (DateTime.UtcNow - _lastSignalTime < MinSignalInterval)
        {
            _logger.LogWarning("Rate limit (1/30min), dropping {Type}", anomaly.Type);
            return;
        }

        // Resolve market name
        var marketInfo = await _resolver.ResolveAsync(anomaly.MarketId);
        var question = marketInfo?.Question ?? anomaly.MarketId;
        var polymarketUrl = marketInfo?.GetPolymarketUrl() ?? "";

        // Execute paper trade
        var direction = signal == "BUY YES" ? "YES" : "NO";
        var buyPrice = GetDecimal(anomaly.Details, "buyPrice")
                       ?? GetDecimal(anomaly.Details, "entryPrice")
                       ?? GetDecimal(anomaly.Details, "yesPrice")
                       ?? anomaly.Severity;

        if (direction == "NO")
            buyPrice = GetDecimal(anomaly.Details, "noPrice") ?? (1.0m - buyPrice);

        var catalyst = GetString(anomaly.Details, "catalyst") ?? anomaly.Type.ToString();
        var hoursToRes = GetDouble(anomaly.Details, "hoursToResolution");

        var paperPosition = _paper.TryEnter(
            anomaly.MarketId, question, direction, buyPrice,
            qualityScore, catalyst, hoursToRes);

        // Format and send alert
        var msg = FormatAlert(anomaly, signal, qualityScore, question, polymarketUrl, paperPosition);
        await _telegram.SendRawAsync(msg);

        _todaySignals++;
        _lastSignalTime = DateTime.UtcNow;

        _logger.LogInformation("SIGNAL SENT [Score:{Score}] {Signal} {Question}",
            qualityScore, signal, question);
    }

    private string FormatAlert(
        AnomalyDetected anomaly,
        string signal,
        int qualityScore,
        string question,
        string polymarketUrl,
        PaperPosition? paperPosition)
    {
        var sb = new StringBuilder();

        // Header
        var scoreEmoji = qualityScore >= 85 ? "\u26a1" : qualityScore >= 70 ? "\ud83d\udfe2" : "\ud83d\udfe1";
        sb.AppendLine($"{scoreEmoji} <b>QUALITY SIGNAL [{qualityScore}/100]</b>");
        sb.AppendLine();

        // Market info
        sb.AppendLine($"\ud83c\udfaf {WebUtility.HtmlEncode(question)}");

        var marketType = GetString(anomaly.Details, "marketType") ?? anomaly.Type.ToString();
        var hoursToRes = GetDouble(anomaly.Details, "hoursToResolution");
        if (hoursToRes.HasValue)
        {
            var resText = hoursToRes.Value < 24
                ? $"{hoursToRes.Value:F0}h"
                : $"{hoursToRes.Value / 24:F0}d";
            sb.AppendLine($"Type: {marketType} | Resolution: {resText}");
        }

        sb.AppendLine();

        // Anomaly details
        sb.AppendLine("<b>\ud83d\udcca ANOMALY:</b>");
        var reasons = GetString(anomaly.Details, "scoreReasons") ?? "";
        if (!string.IsNullOrEmpty(reasons))
        {
            foreach (var reason in reasons.Split('|'))
                sb.AppendLine($"  \u2022 {WebUtility.HtmlEncode(reason.Trim())}");
        }

        // Catalyst
        var catalyst = GetString(anomaly.Details, "catalyst");
        if (!string.IsNullOrEmpty(catalyst))
        {
            sb.AppendLine();
            sb.AppendLine($"\ud83d\udd0d <b>CATALYST:</b> {WebUtility.HtmlEncode(catalyst)}");
        }

        sb.AppendLine();

        // Signal
        var edgeStr = GetDecimal(anomaly.Details, "edge") is decimal edge ? $" (edge: {edge:+0.0%;-0.0%})" : "";
        var roiStr = GetDecimal(anomaly.Details, "expectedROI") is decimal roi ? $" | ROI: +{roi:P0}" : "";
        sb.AppendLine($"\ud83d\udca1 <b>{signal}</b>{edgeStr}{roiStr}");

        // Paper trade info
        if (paperPosition is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"\ud83e\udded <b>PAPER TRADE:</b>");
            sb.AppendLine($"  Entry: {paperPosition.Direction} @ ${paperPosition.EntryPrice:F3}");
            sb.AppendLine($"  Size: ${paperPosition.Size:F2} ({paperPosition.Size / _paper.Balance:P0} of portfolio)");
            sb.AppendLine($"  Balance: ${_paper.Balance:N2} | Open: {_paper.OpenPositionCount}");
        }

        // Link
        if (!string.IsNullOrEmpty(polymarketUrl))
        {
            sb.AppendLine();
            sb.AppendLine($"\ud83d\udd17 <a href=\"{polymarketUrl}\">Polymarket</a>");
        }

        return sb.ToString();
    }

    private static void ResetDayCounterIfNeeded()
    {
        if (DateTime.UtcNow.Date != _todayDate)
        {
            _todayDate = DateTime.UtcNow.Date;
            _todaySignals = 0;
        }
    }

    private static int GetInt(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? Convert.ToInt32(v) : 0;

    private static decimal? GetDecimal(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? Convert.ToDecimal(v) : null;

    private static double? GetDouble(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? Convert.ToDouble(v) : null;

    private static string? GetString(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? v?.ToString() : null;
}
