namespace PolyMarket.Contracts.Models;

public record OddsSnapshot(
    string SourceName,
    string EventId,
    string EventName,
    string Outcome,
    decimal ImpliedProbability,
    decimal RawOdds,
    DateTime Timestamp);
