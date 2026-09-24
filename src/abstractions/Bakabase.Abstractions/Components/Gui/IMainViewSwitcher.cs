namespace Bakabase.Abstractions.Components.Gui;

/// <summary>A place the main window can show: this device's own server, or one it manages.</summary>
/// <param name="Id"><see cref="MainViewTarget.LocalId"/> for this device, otherwise the managed server's id.</param>
public sealed record MainViewTarget(string Id, string Name, bool IsLocal)
{
    public const string LocalId = "local";
}

/// <summary>
/// Which servers the main window can switch between, and where to point it for each.
/// </summary>
/// <remarks>
/// Optional desktop capability, resolved from the host's container: the web UI switches
/// on its own by navigating, and the shell uses this only for its tray menu — the way
/// back when the server being shown runs an older UI with no switcher of its own.
/// </remarks>
public interface IMainViewSwitcher
{
    /// <summary>This device first, then every managed server.</summary>
    /// <remarks>
    /// Called on the UI thread whenever the tray menu opens (macOS) and from a thread-pool
    /// timer every couple of seconds elsewhere, possibly before the host has started. So it
    /// must be thread-safe, return at once, and touch neither the network nor the disk: an
    /// implementation answers from memory.
    /// </remarks>
    IReadOnlyList<MainViewTarget> ListTargets();

    /// <summary>
    /// Where the window should navigate to show <paramref name="targetId"/>, starting the
    /// server's relay if needed: an absolute http(s) URL, for this device exactly the origin
    /// its main window opened at. Null when the target is unknown or not reachable yet.
    /// </summary>
    /// <remarks>
    /// May take time — starting a relay, asking the server for its clock — and so honours
    /// <paramref name="ct"/>: a cancelled token ends it with an
    /// <see cref="OperationCanceledException"/>.
    /// </remarks>
    Task<string?> ResolveUrlAsync(string targetId, CancellationToken ct = default);
}
