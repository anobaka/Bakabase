using Bakabase.Abstractions.Components.Gui;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// What a relay's <c>/client</c> endpoints may ask of the app that runs it: where the other
/// servers are, how they were when last seen, and where this device is. Nothing that pairs,
/// forgets or reads a key.
/// </summary>
public interface IRemoteConsoleNavigator
{
    /// <summary>What this device calls itself.</summary>
    string LocalName { get; }

    /// <summary>This device's own UI origin, or null while it is not known.</summary>
    string? LocalOrigin { get; }

    /// <summary>This device first, then every managed server.</summary>
    IReadOnlyList<MainViewTarget> ListTargets();

    /// <summary>
    /// How a managed server was the last time this device asked it, from memory alone —
    /// never asking it again, never reading the disk. <see cref="ManagedServerState.Unknown"/>
    /// for one not asked since this app started, or not managed at all.
    /// </summary>
    ManagedServerState LastKnownState(string serverId);

    /// <summary>
    /// Where the window should go to show <paramref name="targetId"/>, at
    /// <paramref name="path"/> of its UI. Null for a target this device does not know.
    /// </summary>
    Task<string?> ResolveUrlAsync(string targetId, string? path, CancellationToken ct = default);
}

/// <summary>Which managed server one relay is for, in that relay's own container.</summary>
/// <param name="AppVersion">This app's version, which the relay reports as its own.</param>
public sealed record ConsoleRelayContext(string ServerId, string AppVersion, IRemoteConsoleNavigator Navigator);
