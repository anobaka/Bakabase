using Bakabase.Abstractions.Components.Gui;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// What the shell sees of the console: the places the main window can show, and where each
/// one is. The <see cref="IMainViewSwitcher"/> registered in the app's container.
/// </summary>
/// <remarks>
/// <para>
/// A separate object with no dependencies, rather than the manager itself, because of when
/// and where the shell asks. It reads <see cref="ListTargets"/> on the UI thread whenever the
/// tray menu opens on macOS, and on a thread-pool timer elsewhere — from before the host has
/// started. Resolving the manager at that point would construct it there and then: read the
/// managed-server file, resolve the server's remote-access service and everything behind it.
/// None of that belongs on a UI thread, and a menu must never wait on a disk.
/// </para>
/// <para>
/// So this answers from memory only: this device alone until the manager exists, and from
/// then on the manager's in-memory snapshot, which never touches the disk or the network
/// after the manager's construction. Thread-safe for the same reason — the manager publishes
/// an immutable snapshot on every write, and this object holds nothing else.
/// </para>
/// </remarks>
public sealed class RemoteConsoleSwitcher : IMainViewSwitcher
{
    private static readonly Lazy<string> MachineName = new(() =>
    {
        try
        {
            return Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            return "Bakabase";
        }
    });

    private volatile IRemoteConsoleNavigator? _navigator;

    /// <summary>What this device calls itself — the name its own server gives other devices.</summary>
    public static string LocalName => MachineName.Value;

    /// <summary>Whether the manager exists yet. Before it does, only this device is listed.</summary>
    public bool IsAttached => _navigator != null;

    /// <summary>Called once, by the manager, at the end of its construction.</summary>
    internal void Attach(IRemoteConsoleNavigator navigator) => _navigator = navigator;

    public IReadOnlyList<MainViewTarget> ListTargets() =>
        _navigator?.ListTargets() ?? [new MainViewTarget(MainViewTarget.LocalId, LocalName, true)];

    /// <remarks>
    /// Null before the manager exists: nothing is known yet — not even this device's own
    /// origin, which is recorded when the main window first opens.
    /// </remarks>
    public Task<string?> ResolveUrlAsync(string targetId, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<string?>(ct);
        }

        return _navigator?.ResolveUrlAsync(targetId, null, ct) ?? Task.FromResult<string?>(null);
    }
}
