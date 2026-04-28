using System.Collections.Concurrent;
using Bakabase.Modules.HealthScore.Models;

namespace Bakabase.Modules.HealthScore.Components;

/// <summary>
/// In-memory cache of all profiles, keyed by Id. Profile counts are bounded
/// (typically &lt; 100) so we keep the full set resident. Profile CRUD invalidates
/// via <see cref="Set"/> / <see cref="Remove"/>.
/// </summary>
public sealed class HealthScoreProfileCache
{
    private volatile IReadOnlyDictionary<int, HealthScoreProfile> _byId =
        new Dictionary<int, HealthScoreProfile>();

    public IReadOnlyDictionary<int, HealthScoreProfile> All => _byId;

    public HealthScoreProfile? Find(int id) => _byId.GetValueOrDefault(id);

    public void Replace(IEnumerable<HealthScoreProfile> profiles)
    {
        _byId = profiles.ToDictionary(p => p.Id, p => p);
    }

    public void Set(HealthScoreProfile profile)
    {
        var copy = new ConcurrentDictionary<int, HealthScoreProfile>(_byId);
        copy[profile.Id] = profile;
        _byId = copy;
    }

    public void Remove(int id)
    {
        var copy = new ConcurrentDictionary<int, HealthScoreProfile>(_byId);
        copy.TryRemove(id, out _);
        _byId = copy;
    }
}
