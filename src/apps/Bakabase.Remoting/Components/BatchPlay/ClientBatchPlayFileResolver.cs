using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Paths;
using Bakabase.Remoting.Components.UserMachine;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Components;

namespace Bakabase.Remoting.Components.BatchPlay;

/// <summary>
/// Turns a server path into something a player on this machine can open.
/// </summary>
/// <remarks>
/// <para>
/// The same two routes single-file playback takes, for the same reasons. A mapped file
/// that is really there is handed over as a path, so the player reads from disk. Anything
/// else — no mapping for that library, or a mapping gone stale — becomes a URL back
/// through this client, which signs and relays it.
/// </para>
/// <para>
/// Streaming is available for media only. Programs and other file kinds still need a
/// mapped file; returning an HTTP URL cannot supply their working folder or dependencies.
/// </para>
/// </remarks>
public sealed class ClientBatchPlayFileResolver(
    ActiveConnection connection,
    ILoopbackAddressProvider loopback, ManagedBatchPlayContext? owner = null) : IBatchPlayFileResolver
{
    public string? Resolve(string path)
    {
        var mapped = ClientPathMapper.Map(path, connection.Server?.PathMappings ?? []);

        if (mapped.Mapped && File.Exists(mapped.LocalPath)) return ToOsSafePath(mapped.LocalPath!);
        if (!PlayerFileCapabilities.CanStream(path)) return null;
        var url = loopback.BuildRawFileUrl(path);
        return string.IsNullOrEmpty(owner?.LibraryEpoch) ? url : url +
            (url.Contains('?') ? "&" : "?") + "managedLibraryEpoch=" + Uri.EscapeDataString(owner.LibraryEpoch);
    }

    private static string ToOsSafePath(string path) =>
        OperatingSystem.IsWindows()
            ? path.Replace(InternalOptions.DirSeparator, InternalOptions.WindowsSpecificDirSeparator)
            : path;
}
