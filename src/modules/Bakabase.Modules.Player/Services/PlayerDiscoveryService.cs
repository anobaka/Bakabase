using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Services;
using Bakabase.Modules.Player.Components;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Player.Services;

/// <summary>
/// Singleton. Scans lazily on first use and caches the result; a rescan can
/// be requested explicitly (e.g. after the user installs a player).
/// </summary>
public class PlayerDiscoveryService(
    IPlayerExecutableLocator locator,
    ILogger<PlayerDiscoveryService> logger) : IPlayerDiscoveryService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<DiscoveredPlayer>? _cache;

    public async Task<IReadOnlyList<DiscoveredPlayer>> GetDiscoveredPlayersAsync(bool rescan = false,
        CancellationToken ct = default)
    {
        if (!rescan && _cache != null)
        {
            return _cache;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (rescan || _cache == null)
            {
                _cache = Scan();
            }

            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    private List<DiscoveredPlayer> Scan()
    {
        var players = new List<DiscoveredPlayer>();
        foreach (var definition in KnownPlayerDefinitions.All)
        {
            try
            {
                var path = locator.Locate(definition).FirstOrDefault();
                if (path != null)
                {
                    players.Add(new DiscoveredPlayer
                    {
                        DefinitionId = definition.Id,
                        DisplayName = definition.DisplayName,
                        ExecutablePath = path,
                        Capabilities = definition.Capabilities,
                    });
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Probing for player {Player} failed", definition.Id);
            }
        }

        logger.LogInformation("Discovered {Count} known players: {Players}", players.Count,
            string.Join(", ", players.Select(p => p.DefinitionId)));
        return players;
    }
}
