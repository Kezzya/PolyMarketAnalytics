using System.Globalization;
using System.Text.RegularExpressions;

namespace PolyMarket.Analytics.Services;

/// <summary>
/// Parses Polymarket question text to extract crypto market parameters.
/// Matches questions like:
///   "Will Bitcoin be above $110,000 on March 31?"
///   "Will ETH hit $4,000 by June 30, 2025?"
///   "Bitcoin above $100k on February 28?"
///   "Will the price of SOL be above $200 on March 31?"
/// </summary>
public class CryptoMarketMatcher
{
    // Crypto name → canonical symbol
    private static readonly Dictionary<string, string> CryptoAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bitcoin"] = "BTC", ["btc"] = "BTC",
        ["ethereum"] = "ETH", ["eth"] = "ETH", ["ether"] = "ETH",
        ["solana"] = "SOL", ["sol"] = "SOL",
        ["dogecoin"] = "DOGE", ["doge"] = "DOGE",
        ["xrp"] = "XRP", ["ripple"] = "XRP",
        ["polygon"] = "MATIC", ["matic"] = "MATIC",
        ["sui"] = "SUI",
    };

    // Regex for price: $110,000 or $110000 or $110k or $4,000.50
    private static readonly Regex PriceRegex = new(
        @"\$\s*([\d,]+\.?\d*)\s*(k|K|m|M)?",
        RegexOptions.Compiled);

    // Regex for "above" / "below" / "hit" / "reach"
    private static readonly Regex DirectionRegex = new(
        @"\b(above|over|exceed|hit|reach|surpass|higher than|more than|at least)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BelowRegex = new(
        @"\b(below|under|less than|lower than|drop to|fall to|dip to|beneath|crash to)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Date patterns
    private static readonly Regex DateRegex = new(
        @"(?:on|by|before)\s+(\w+\s+\d{1,2}(?:,?\s*\d{4})?)|(\w+\s+\d{1,2}(?:st|nd|rd|th)?,?\s*\d{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Month abbreviation handling
    private static readonly string[] DateFormats =
    [
        "MMMM d, yyyy", "MMMM d yyyy", "MMMM d",
        "MMM d, yyyy", "MMM d yyyy", "MMM d",
        "MMMM dd, yyyy", "MMMM dd yyyy", "MMMM dd",
    ];

    public CryptoMarketMatch? TryMatch(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return null;

        // 1. Find the crypto asset
        string? symbol = null;
        foreach (var (alias, sym) in CryptoAliases)
        {
            if (Regex.IsMatch(question, $@"\b{Regex.Escape(alias)}\b", RegexOptions.IgnoreCase))
            {
                symbol = sym;
                break;
            }
        }

        if (symbol is null)
            return null;

        // 2. Extract target price
        var priceMatch = PriceRegex.Match(question);
        if (!priceMatch.Success)
            return null;

        var priceStr = priceMatch.Groups[1].Value.Replace(",", "");
        if (!decimal.TryParse(priceStr, CultureInfo.InvariantCulture, out var targetPrice))
            return null;

        // Handle k/K/m/M suffix
        var suffix = priceMatch.Groups[2].Value.ToUpperInvariant();
        if (suffix == "K") targetPrice *= 1000;
        if (suffix == "M") targetPrice *= 1_000_000;

        if (targetPrice <= 0)
            return null;

        // 3. Determine direction (above/below)
        // Check "below" first — "dip to" is more specific than generic "reach"
        var isBelow = BelowRegex.IsMatch(question);
        var isAbove = !isBelow && DirectionRegex.IsMatch(question);

        // Default to "above" if neither found (most common on Polymarket)
        if (!isAbove && !isBelow)
            isAbove = true;

        // 4. Extract expiry date
        DateTime? expiryDate = null;
        var dateMatch = DateRegex.Match(question);
        if (dateMatch.Success)
        {
            var dateStr = (dateMatch.Groups[1].Success ? dateMatch.Groups[1].Value : dateMatch.Groups[2].Value)
                .Trim()
                .Replace("st", "").Replace("nd", "").Replace("rd", "").Replace("th", "");

            foreach (var fmt in DateFormats)
            {
                if (DateTime.TryParseExact(dateStr, fmt, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed))
                {
                    // If no year specified, assume current or next year
                    if (parsed.Year == 1)
                        parsed = new DateTime(DateTime.UtcNow.Year, parsed.Month, parsed.Day);
                    if (parsed < DateTime.UtcNow)
                        parsed = parsed.AddYears(1);

                    expiryDate = parsed;
                    break;
                }
            }
        }

        // If no date found, try endDate from market metadata (caller should handle)
        // We still return a match with null expiryDate

        return new CryptoMarketMatch(
            Symbol: symbol,
            TargetPrice: targetPrice,
            IsAbove: isAbove,
            ExpiryDate: expiryDate);
    }
}

public record CryptoMarketMatch(
    string Symbol,
    decimal TargetPrice,
    bool IsAbove,
    DateTime? ExpiryDate);
