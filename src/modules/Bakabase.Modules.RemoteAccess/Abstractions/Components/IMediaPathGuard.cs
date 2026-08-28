namespace Bakabase.Modules.RemoteAccess.Abstractions.Components;

/// <summary>
/// Decides whether a filesystem path may be read on behalf of a remote client.
/// <para>
/// Bakabase's file endpoints are path-addressed (<c>/file/play?fullname=…</c>,
/// <c>/tool/thumbnail?path=…</c>), which is harmless while only the host's own
/// browser can reach them and an arbitrary-file-read the moment a phone can. The
/// guard closes that by requiring the path to sit under a root the user has
/// actually pointed Bakabase at.
/// </para>
/// </summary>
public interface IMediaPathGuard
{
    /// <summary>
    /// True when <paramref name="path"/> resolves to a location under one of the
    /// servable roots. A null, empty, relative, or out-of-tree path is false.
    /// Paths using the <c>archive.zip!entry</c> syntax are judged by their archive.
    /// </summary>
    Task<bool> IsServableAsync(string? path, CancellationToken ct = default);

    /// <summary>
    /// The current root set, normalized. Exposed for diagnostics and tests.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetRootsAsync(CancellationToken ct = default);

    /// <summary>
    /// Drops the cached root set so the next check re-reads media libraries and
    /// path marks. Call after a library or mark changes.
    /// </summary>
    void Invalidate();
}
