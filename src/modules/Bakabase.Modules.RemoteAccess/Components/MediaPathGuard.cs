using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <inheritdoc />
public class MediaPathGuard(
    IEnumerable<IServableRootProvider> rootProviders,
    ILogger<MediaPathGuard> logger) : IMediaPathGuard
{
    /// <summary>
    /// Roots change when a library or path mark changes, which the app signals via
    /// <see cref="Invalidate"/>. The TTL is a backstop for anything that forgets to.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IServableRootProvider[] _rootProviders = rootProviders.ToArray();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private IReadOnlyCollection<string> _roots = [];
    private DateTime _rootsLoadedAt = DateTime.MinValue;
    private bool _loaded;

    public async Task<bool> IsServableAsync(string? path, CancellationToken ct = default)
    {
        var normalized = RemotePathNormalizer.Normalize(path);
        if (normalized == null)
        {
            return false;
        }

        var roots = await GetRootsAsync(ct);
        return roots.Any(root => RemotePathNormalizer.IsUnder(normalized, root));
    }

    public async Task<IReadOnlyCollection<string>> GetRootsAsync(CancellationToken ct = default)
    {
        if (_loaded && DateTime.UtcNow - _rootsLoadedAt < CacheTtl)
        {
            return _roots;
        }

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed while we waited.
            if (_loaded && DateTime.UtcNow - _rootsLoadedAt < CacheTtl)
            {
                return _roots;
            }

            var roots = new HashSet<string>();
            foreach (var provider in _rootProviders)
            {
                try
                {
                    foreach (var root in await provider.GetRootsAsync(ct))
                    {
                        var normalized = RemotePathNormalizer.Normalize(root);
                        if (normalized != null)
                        {
                            roots.Add(normalized);
                        }
                    }
                }
                catch (Exception e)
                {
                    // A provider that cannot answer must not widen the root set, and
                    // must not take down file serving either — skip it and keep the
                    // roots we do know about.
                    logger.LogError(e, "Servable root provider {Provider} failed; its roots are omitted",
                        provider.GetType().Name);
                }
            }

            _roots = roots;
            _rootsLoadedAt = DateTime.UtcNow;
            _loaded = true;
            return _roots;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Invalidate()
    {
        _loaded = false;
        _rootsLoadedAt = DateTime.MinValue;
    }
}
