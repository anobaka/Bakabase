using System.Text.Json;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>
/// Remembers which loopback port this client actually bound, so it asks for the same one
/// next launch.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LoopbackPortAllocator.PreferredPort"/> already clears the all-in-one's own
/// window, which is what made the port move in the first place. This covers what is left:
/// any other program holding the preferred port for one launch pushes the client forward,
/// and with no memory it would come straight back the moment that program stopped. The
/// browser keys localStorage, IndexedDB and its cache to the origin, so it reads both
/// moves as a different client. Asking for whatever was bound last means a move happens
/// once instead of flapping.
/// </para>
/// <para>
/// A plain file rather than an <c>[Options]</c> class or <c>IClientDataDirectory</c>: the
/// port is decided while the host is still being built, before there is a container to
/// resolve either from.
/// </para>
/// <para>
/// Every failure is swallowed on purpose. A client that cannot read or write this file
/// still has a port to bind, and refusing to start over a cache would trade a cosmetic
/// problem for a fatal one. A half-written file reads as no memory at all, which is
/// exactly what the first launch reads, so the write needs no atomicity to be safe.
/// </para>
/// </remarks>
public sealed class LoopbackPortMemory(string directoryPath)
{
    public const string FileName = "host.json";

    private string FilePath => Path.Combine(directoryPath, FileName);

    private sealed record State
    {
        public int LoopbackPort { get; init; }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() {WriteIndented = true};

    /// <summary>
    /// The port last bound, or null when there is nothing usable to go on.
    /// </summary>
    public int? Read()
    {
        try
        {
            var path = FilePath;

            if (!File.Exists(path))
            {
                return null;
            }

            var port = JsonSerializer.Deserialize<State>(File.ReadAllText(path), SerializerOptions)?.LoopbackPort;

            // A port of 0 is what an absent property deserializes to, and anything outside
            // the valid range is a file somebody edited. Both mean "no answer" rather than
            // an answer to bind against.
            return port is > 0 and <= 65535 ? port : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Records <paramref name="port"/> as the one to ask for next time. A no-op when it is
    /// already what the file says, so a client that never moves never touches the disk.
    /// </summary>
    public void Write(int port)
    {
        try
        {
            if (Read() == port)
            {
                return;
            }

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new State {LoopbackPort = port}, SerializerOptions));
        }
        catch
        {
            // See the class remarks: the port is already chosen and bound by the time this
            // runs. Losing the memory costs one origin change, losing the process costs the
            // session.
        }
    }
}
