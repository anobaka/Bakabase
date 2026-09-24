using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Paths;
using Bakabase.Remoting.Components.UserMachine;
using Bakabase.Modules.Player.Abstractions.Components;

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
/// So this never answers null, and batch play never reports files as missing on a client.
/// That is the honest answer: the file exists, it is just not on this computer, and the
/// user asked to play it rather than to be told where it isn't.
/// </para>
/// </remarks>
public sealed class ClientBatchPlayFileResolver(
    ActiveConnection connection,
    ILoopbackAddressProvider loopback) : IBatchPlayFileResolver
{
    public string? Resolve(string path)
    {
        var mapped = ClientPathMapper.Map(path, connection.Server?.PathMappings ?? []);

        return mapped.Mapped && File.Exists(mapped.LocalPath)
            ? ToOsSafePath(mapped.LocalPath!)
            : loopback.BuildRawFileUrl(path);
    }

    private static string ToOsSafePath(string path) =>
        OperatingSystem.IsWindows()
            ? path.Replace(InternalOptions.DirSeparator, InternalOptions.WindowsSpecificDirSeparator)
            : path;
}
