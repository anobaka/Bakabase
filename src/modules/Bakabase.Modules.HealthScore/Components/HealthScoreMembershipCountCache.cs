using System.Collections.Concurrent;

namespace Bakabase.Modules.HealthScore.Components;

/// <summary>
/// In-memory record of how many resources the most recent scoring run matched
/// per profile. Surfaced to the UI as a hint; null until the profile has been
/// scored at least once since startup. Not persisted — restarts clear it.
/// </summary>
public sealed class HealthScoreMembershipCountCache
{
    private readonly ConcurrentDictionary<int, int> _byProfile = new();

    public int? Get(int profileId) =>
        _byProfile.TryGetValue(profileId, out var v) ? v : null;

    public void Set(int profileId, int count) => _byProfile[profileId] = count;

    public void Clear(int profileId) => _byProfile.TryRemove(profileId, out _);
}
