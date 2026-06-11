using Bakabase.Modules.Player.Abstractions.Models.Domain;

namespace Bakabase.Modules.Player.Abstractions.Services;

/// <summary>
/// Scans the machine for known players and caches the result for the
/// lifetime of the process (or until a rescan is requested).
/// </summary>
public interface IPlayerDiscoveryService
{
    Task<IReadOnlyList<DiscoveredPlayer>> GetDiscoveredPlayersAsync(bool rescan = false,
        CancellationToken ct = default);
}
