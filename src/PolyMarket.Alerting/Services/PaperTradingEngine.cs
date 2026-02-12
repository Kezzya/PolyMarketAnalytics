using System.Collections.Concurrent;
using System.Text.Json;

namespace PolyMarket.Alerting.Services;

/// <summary>
/// Paper trading engine — virtual $1000 portfolio.
/// Tracks positions, P&L, win/loss ratio.
/// Logs every trade to JSON file for analysis.
/// </summary>
public class PaperTradingEngine
{
    private readonly ILogger<PaperTradingEngine> _logger;
    private readonly string _tradesFilePath;

    private decimal _balance;
    private readonly decimal _startingBalance;
    private readonly ConcurrentDictionary<string, PaperPosition> _openPositions = new();
    private readonly List<PaperTrade> _closedTrades = [];
    private readonly HashSet<string> _tradedMarketIds = new(); // prevents re-entry into same markets
    private int _lossStreak = 0;
    private bool _paused = false;
    private DateTime? _pausedUntil;

    // Limits
    private const int MaxOpenPositions = 3;
    private const decimal MaxRiskPercent = 0.15m; // 15% of portfolio
    private const int MaxLossStreak = 5;
    private const decimal PauseDrawdownPercent = 0.20m; // -20% → pause 3 days

    public PaperTradingEngine(ILogger<PaperTradingEngine> logger, IConfiguration config)
    {
        _logger = logger;
        _startingBalance = decimal.Parse(config["PaperTrading:StartingBalance"] ?? "1000");
        _balance = _startingBalance;
        _tradesFilePath = config["PaperTrading:TradesFile"] ?? "/app/data/paper_trades.json";

        // Try to load existing state
        LoadState();
    }

    public decimal Balance => _balance;
    public int OpenPositionCount => _openPositions.Count;
    public IReadOnlyCollection<PaperPosition> OpenPositions => _openPositions.Values.ToList();
    public IReadOnlyCollection<PaperTrade> ClosedTrades => _closedTrades.AsReadOnly();

    /// <summary>
    /// Execute a paper trade based on quality score.
    /// Returns the position if executed, null if blocked.
    /// </summary>
    public PaperPosition? TryEnter(
        string marketId,
        string question,
        string direction,  // "YES" or "NO"
        decimal entryPrice,
        int qualityScore,
        string catalyst,
        double? hoursToResolution)
    {
        // Risk checks
        if (_paused && _pausedUntil.HasValue && DateTime.UtcNow < _pausedUntil.Value)
        {
            _logger.LogWarning("Paper trading PAUSED until {Until}", _pausedUntil.Value);
            return null;
        }
        _paused = false;

        if (_openPositions.Count >= MaxOpenPositions)
        {
            _logger.LogWarning("Max open positions ({Max}), skipping", MaxOpenPositions);
            return null;
        }

        if (_openPositions.ContainsKey(marketId))
        {
            _logger.LogDebug("Already have open position in {MarketId}", marketId);
            return null;
        }

        if (_tradedMarketIds.Contains(marketId))
        {
            _logger.LogDebug("Already traded this market, skipping re-entry: {MarketId}", marketId);
            return null;
        }

        if (_lossStreak >= MaxLossStreak)
        {
            _logger.LogWarning("Loss streak {Count} >= {Max}, pausing", _lossStreak, MaxLossStreak);
            _paused = true;
            _pausedUntil = DateTime.UtcNow.AddDays(1);
            return null;
        }

        // Check drawdown
        var drawdown = (_startingBalance - _balance) / _startingBalance;
        if (drawdown >= PauseDrawdownPercent)
        {
            _logger.LogWarning("Drawdown {DD:P0} >= {Max:P0}, pausing 3 days",
                drawdown, PauseDrawdownPercent);
            _paused = true;
            _pausedUntil = DateTime.UtcNow.AddDays(3);
            return null;
        }

        // Position sizing based on quality score
        decimal sizePercent;
        if (qualityScore >= 85) sizePercent = 0.05m;       // 5% of portfolio
        else if (qualityScore >= 70) sizePercent = 0.03m;   // 3%
        else sizePercent = 0.02m;                            // 2%

        var positionSize = Math.Round(_balance * sizePercent, 2);
        positionSize = Math.Max(5m, Math.Min(positionSize, 50m)); // $5 min, $50 max

        // Check total risk
        var currentRisk = _openPositions.Values.Sum(p => p.Size);
        if ((currentRisk + positionSize) / _balance > MaxRiskPercent)
        {
            positionSize = Math.Round(_balance * MaxRiskPercent - currentRisk, 2);
            if (positionSize < 5m)
            {
                _logger.LogWarning("Risk limit reached, skipping");
                return null;
            }
        }

        var shares = Math.Round(positionSize / entryPrice, 2);

        var position = new PaperPosition
        {
            MarketId = marketId,
            Question = question,
            Direction = direction,
            EntryPrice = entryPrice,
            Size = positionSize,
            Shares = shares,
            QualityScore = qualityScore,
            Catalyst = catalyst,
            HoursToResolution = hoursToResolution,
            EntryTime = DateTime.UtcNow
        };

        _openPositions[marketId] = position;
        _tradedMarketIds.Add(marketId);
        _balance -= positionSize; // Reserve capital for position

        _logger.LogInformation(
            "PAPER ENTRY: {Dir} ${Size} @ {Price:F3} | Score={Score} | Balance=${Balance:N2} | {Question}",
            direction, positionSize, entryPrice, qualityScore, _balance, question);

        SaveState();
        return position;
    }

    /// <summary>
    /// Update positions with current prices.
    /// Handles take-profit and stop-loss.
    /// </summary>
    public PaperTrade? CheckAndClose(string marketId, decimal currentPrice, string? exitReason = null)
    {
        if (!_openPositions.TryGetValue(marketId, out var position))
            return null;

        var currentValue = position.Shares * currentPrice;
        var unrealizedPnL = currentValue - position.Size;
        var unrealizedPnLPercent = unrealizedPnL / position.Size;

        // Stop-loss: -40%
        if (unrealizedPnLPercent <= -0.40m && exitReason is null)
        {
            exitReason = "STOP_LOSS (-40%)";
        }

        // Take-profit: +50% → close full position
        if (unrealizedPnLPercent >= 0.50m && exitReason is null)
        {
            exitReason = "TAKE_PROFIT (+50%)";
        }

        // Resolution
        if (exitReason is not null)
        {
            return ClosePosition(marketId, currentPrice, exitReason);
        }

        return null;
    }

    /// <summary>
    /// Force close at resolution (price = 1.0 or 0.0).
    /// </summary>
    public PaperTrade? CloseAtResolution(string marketId, bool wonBet)
    {
        var exitPrice = wonBet ? 1.0m : 0.0m;
        return ClosePosition(marketId, exitPrice, "RESOLUTION");
    }

    private PaperTrade? ClosePosition(string marketId, decimal exitPrice, string exitReason)
    {
        if (!_openPositions.TryRemove(marketId, out var position))
            return null;

        var exitValue = position.Shares * exitPrice;
        var pnlDollars = exitValue - position.Size;
        var pnlPercent = pnlDollars / position.Size;
        var isWin = pnlDollars > 0;

        _balance += position.Size + pnlDollars; // return capital + P&L

        if (isWin)
            _lossStreak = 0;
        else
            _lossStreak++;

        var trade = new PaperTrade
        {
            MarketId = position.MarketId,
            Question = position.Question,
            Direction = position.Direction,
            QualityScore = position.QualityScore,
            Catalyst = position.Catalyst,
            HoursToResolution = position.HoursToResolution,

            EntryPrice = position.EntryPrice,
            EntrySize = position.Size,
            Shares = position.Shares,
            EntryTime = position.EntryTime,

            ExitPrice = exitPrice,
            ExitReason = exitReason,
            ExitTime = DateTime.UtcNow,

            PnLDollars = pnlDollars,
            PnLPercent = pnlPercent,
            IsWin = isWin,
            BalanceAfter = _balance
        };

        _closedTrades.Add(trade);

        _logger.LogInformation(
            "PAPER EXIT: {Outcome} | {Dir} {Question} | Entry={Entry:F3} Exit={Exit:F3} | P&L=${PnL:+0.00;-0.00} ({PnLPct:+0.0%;-0.0%}) | Balance=${Balance:N2}",
            isWin ? "WIN" : "LOSS", position.Direction, position.Question,
            position.EntryPrice, exitPrice, pnlDollars, pnlPercent, _balance);

        SaveState();
        return trade;
    }

    /// <summary>
    /// Generate daily report summary.
    /// </summary>
    public DailyReport GetDailyReport()
    {
        var today = DateTime.UtcNow.Date;
        var todayTrades = _closedTrades.Where(t => t.ExitTime.Date == today).ToList();
        var allTrades = _closedTrades;

        return new DailyReport
        {
            Date = today,
            Balance = _balance,
            StartingBalance = _startingBalance,
            TotalPnL = _balance - _startingBalance,
            TotalPnLPercent = (_balance - _startingBalance) / _startingBalance,

            TodayTrades = todayTrades,
            TodayWins = todayTrades.Count(t => t.IsWin),
            TodayLosses = todayTrades.Count(t => !t.IsWin),
            TodayPnL = todayTrades.Sum(t => t.PnLDollars),

            TotalTrades = allTrades.Count,
            TotalWins = allTrades.Count(t => t.IsWin),
            TotalLosses = allTrades.Count(t => !t.IsWin),
            WinRate = allTrades.Count > 0
                ? (decimal)allTrades.Count(t => t.IsWin) / allTrades.Count
                : 0,
            AvgWinPnL = allTrades.Where(t => t.IsWin).Select(t => t.PnLPercent).DefaultIfEmpty(0).Average(),
            AvgLossPnL = allTrades.Where(t => !t.IsWin).Select(t => t.PnLPercent).DefaultIfEmpty(0).Average(),

            OpenPositions = _openPositions.Values.ToList(),
            LossStreak = _lossStreak,
            IsPaused = _paused
        };
    }

    private void SaveState()
    {
        try
        {
            var dir = Path.GetDirectoryName(_tradesFilePath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var state = new PaperTradingState
            {
                Balance = _balance,
                OpenPositions = _openPositions.Values.ToList(),
                ClosedTrades = _closedTrades,
                TradedMarketIds = _tradedMarketIds.ToList(),
                LossStreak = _lossStreak,
                Paused = _paused,
                PausedUntil = _pausedUntil
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_tradesFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save paper trading state");
        }
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(_tradesFilePath))
                return;

            var json = File.ReadAllText(_tradesFilePath);
            var state = JsonSerializer.Deserialize<PaperTradingState>(json);
            if (state is null) return;

            _balance = state.Balance;
            _lossStreak = state.LossStreak;
            _paused = state.Paused;
            _pausedUntil = state.PausedUntil;

            foreach (var pos in state.OpenPositions)
                _openPositions[pos.MarketId] = pos;

            _closedTrades.AddRange(state.ClosedTrades);

            // Restore traded market IDs (from state + rebuild from closed trades for migration)
            if (state.TradedMarketIds is { Count: > 0 })
            {
                foreach (var id in state.TradedMarketIds)
                    _tradedMarketIds.Add(id);
            }
            // Also rebuild from closed trades (in case TradedMarketIds was missing in old state)
            foreach (var trade in _closedTrades)
                _tradedMarketIds.Add(trade.MarketId);
            foreach (var pos in _openPositions.Values)
                _tradedMarketIds.Add(pos.MarketId);

            // One-time migration: fix inflated balance from bug where TryEnter didn't deduct size
            // Detect: if no open positions but balance > starting + sum(closed PnL), it's inflated
            if (_openPositions.IsEmpty && _closedTrades.Count > 0)
            {
                var expectedBalance = _startingBalance + _closedTrades.Sum(t => t.PnLDollars);
                if (_balance > expectedBalance + 0.01m)
                {
                    _logger.LogWarning(
                        "Balance migration: ${Old:N2} → ${New:N2} (was inflated by ${Diff:N2} due to missing entry deduction)",
                        _balance, expectedBalance, _balance - expectedBalance);
                    _balance = expectedBalance;
                    SaveState(); // persist corrected balance
                }
            }

            _logger.LogInformation("Loaded paper trading state: balance=${Balance:N2}, {Open} open, {Closed} closed",
                _balance, _openPositions.Count, _closedTrades.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load paper trading state, starting fresh");
        }
    }
}

public class PaperPosition
{
    public string MarketId { get; set; } = "";
    public string Question { get; set; } = "";
    public string Direction { get; set; } = "";  // "YES" or "NO"
    public decimal EntryPrice { get; set; }
    public decimal Size { get; set; }  // $ amount
    public decimal Shares { get; set; }
    public int QualityScore { get; set; }
    public string Catalyst { get; set; } = "";
    public double? HoursToResolution { get; set; }
    public DateTime EntryTime { get; set; }
}

public class PaperTrade
{
    public string MarketId { get; set; } = "";
    public string Question { get; set; } = "";
    public string Direction { get; set; } = "";
    public int QualityScore { get; set; }
    public string Catalyst { get; set; } = "";
    public double? HoursToResolution { get; set; }

    public decimal EntryPrice { get; set; }
    public decimal EntrySize { get; set; }
    public decimal Shares { get; set; }
    public DateTime EntryTime { get; set; }

    public decimal ExitPrice { get; set; }
    public string ExitReason { get; set; } = "";
    public DateTime ExitTime { get; set; }

    public decimal PnLDollars { get; set; }
    public decimal PnLPercent { get; set; }
    public bool IsWin { get; set; }
    public decimal BalanceAfter { get; set; }
}

public class DailyReport
{
    public DateTime Date { get; set; }
    public decimal Balance { get; set; }
    public decimal StartingBalance { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal TotalPnLPercent { get; set; }

    public List<PaperTrade> TodayTrades { get; set; } = [];
    public int TodayWins { get; set; }
    public int TodayLosses { get; set; }
    public decimal TodayPnL { get; set; }

    public int TotalTrades { get; set; }
    public int TotalWins { get; set; }
    public int TotalLosses { get; set; }
    public decimal WinRate { get; set; }
    public decimal AvgWinPnL { get; set; }
    public decimal AvgLossPnL { get; set; }

    public List<PaperPosition> OpenPositions { get; set; } = [];
    public int LossStreak { get; set; }
    public bool IsPaused { get; set; }
}

public class PaperTradingState
{
    public decimal Balance { get; set; }
    public List<PaperPosition> OpenPositions { get; set; } = [];
    public List<PaperTrade> ClosedTrades { get; set; } = [];
    public List<string> TradedMarketIds { get; set; } = [];
    public int LossStreak { get; set; }
    public bool Paused { get; set; }
    public DateTime? PausedUntil { get; set; }
}
