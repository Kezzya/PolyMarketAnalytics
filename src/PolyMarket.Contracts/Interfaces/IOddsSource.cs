using PolyMarket.Contracts.Models;

namespace PolyMarket.Contracts.Interfaces;

public interface IOddsSource
{
    string Name { get; }
    Task<IReadOnlyList<OddsSnapshot>> GetCurrentOddsAsync(CancellationToken ct);
}
