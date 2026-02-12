using System.Net;
using System.Text;
using System.Text.Json;
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

    // Rate limiting — persisted to file so restarts don't reset
    private static readonly object _rateLock = new();
    private const int MaxSignalsPerDay = 5;
    private static readonly TimeSpan MinSignalInterval = TimeSpan.FromMinutes(30);
    private const string RateLimitFile = "/app/data/rate_limit.json";

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

        // ═══════════════════════════════════════
        // HARD GATE: must have qualityScore >= 60
        // Old detectors without quality scoring are DROPPED
        // ═══════════════════════════════════════
        var qualityScore = GetInt(anomaly.Details, "qualityScore");
        if (qualityScore < 60)
            return; // silent drop — old detectors don't pass this

        // Must have signal (BUY YES / BUY NO)
        var signal = GetString(anomaly.Details, "signal") ?? "";
        if (signal != "BUY YES" && signal != "BUY NO")
            return;

        // ═══════════════════════════════════════
        // RATE LIMITING — persisted across restarts
        // ═══════════════════════════════════════
        var rateState = LoadRateLimit();
        if (rateState.Date != DateTime.UtcNow.Date)
        {
            rateState = new RateLimitState { Date = DateTime.UtcNow.Date };
        }

        if (rateState.TodayCount >= MaxSignalsPerDay)
        {
            _logger.LogWarning("Daily limit ({Max}/day), dropping {Type}", MaxSignalsPerDay, anomaly.Type);
            return;
        }

        if (rateState.LastSignalTime.HasValue &&
            DateTime.UtcNow - rateState.LastSignalTime.Value < MinSignalInterval)
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

        // Update rate limit — persist to disk
        rateState.TodayCount++;
        rateState.LastSignalTime = DateTime.UtcNow;
        SaveRateLimit(rateState);

        _logger.LogInformation("SIGNAL SENT [{Count}/{Max}] [Score:{Score}] {Signal} {Question}",
            rateState.TodayCount, MaxSignalsPerDay, qualityScore, signal, question);
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

        // Context — edge, volatility, crypto price, days to expiry
        sb.AppendLine("<b>\ud83d\udcca CONTEXT:</b>");
        var symbol = GetString(anomaly.Details, "symbol");
        var cryptoPrice = GetDecimal(anomaly.Details, "currentCryptoPrice");
        var targetPrice = GetDecimal(anomaly.Details, "targetPrice");
        var fairValue = GetDecimal(anomaly.Details, "fairValue");
        var yesPrice = GetDecimal(anomaly.Details, "yesPrice");
        var volatility = GetDecimal(anomaly.Details, "volatility");
        var daysToExpiry = GetDouble(anomaly.Details, "daysToExpiry");
        var edge = GetDecimal(anomaly.Details, "edge");
        var absEdge = GetDecimal(anomaly.Details, "absEdge");

        if (symbol != null && cryptoPrice.HasValue)
            sb.AppendLine($"  {symbol}: ${cryptoPrice.Value:N2} (target ${targetPrice ?? 0:N0})");
        if (fairValue.HasValue && yesPrice.HasValue)
            sb.AppendLine($"  Fair: {fairValue.Value:P0} vs Market: {yesPrice.Value:P0}");
        if (absEdge.HasValue)
            sb.AppendLine($"  Edge: {absEdge.Value:P1}");
        if (volatility.HasValue)
            sb.AppendLine($"  Volatility: {volatility.Value:P0} ann.");
        if (daysToExpiry.HasValue)
            sb.AppendLine($"  Expiry: {daysToExpiry.Value:F0} days");

        // Score breakdown
        var reasons = GetString(anomaly.Details, "scoreReasons") ?? "";
        if (!string.IsNullOrEmpty(reasons))
        {
            sb.AppendLine();
            sb.AppendLine("<b>\ud83c\udfaf SCORE BREAKDOWN:</b>");
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
        var roiStr = GetDecimal(anomaly.Details, "expectedROI") is decimal roi ? $" | ROI: +{roi:P0}" : "";
        sb.AppendLine($"\ud83d\udca1 <b>{signal}</b>{roiStr}");

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

    // ═══════════════════════════════════════
    // Rate limit persistence
    // ═══════════════════════════════════════

    private static RateLimitState LoadRateLimit()
    {
        lock (_rateLock)
        {
            try
            {
                if (File.Exists(RateLimitFile))
                {
                    var json = File.ReadAllText(RateLimitFile);
                    return JsonSerializer.Deserialize<RateLimitState>(json) ?? new();
                }
            }
            catch { }
            return new RateLimitState { Date = DateTime.UtcNow.Date };
        }
    }

    private static void SaveRateLimit(RateLimitState state)
    {
        lock (_rateLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(RateLimitFile);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(RateLimitFile,
                    JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
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

public class RateLimitState
{
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    public int TodayCount { get; set; }
    public DateTime? LastSignalTime { get; set; }
}
