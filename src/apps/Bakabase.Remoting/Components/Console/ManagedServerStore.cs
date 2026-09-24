using System.Text.Json;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// Where the desktop app keeps the servers it manages: <c>{AppData}/remote-access/managed</c>.
/// </summary>
/// <remarks>
/// Resolved on every use rather than once, because the data directory can be relocated
/// while the app runs and the store must follow the rest of the app's state there.
/// <see cref="Ensure"/> creates the directory owner-only on Unix: what goes in it is keys.
/// </remarks>
public sealed class ManagedServerDirectory(Func<string> resolve) : IClientDataDirectory
{
    /// <summary>Under the server's own <c>remote-access</c> directory.</summary>
    public const string DirectoryName = "managed";

    public string Path => resolve();

    public string Ensure()
    {
        var path = resolve();

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}

/// <summary>
/// The servers this device manages, and the keys it manages them with.
/// </summary>
/// <remarks>
/// <para>
/// The same file format as the removed thin client's <c>connection.json</c> — it is
/// <see cref="ClientConnectionStore"/> underneath, with its atomic writes and its
/// owner-only file mode — so importing an old install's pairings
/// (<see cref="LegacyClientConnectionSource"/>) is a copy rather than a translation.
/// </para>
/// <para>
/// A distinct type on purpose, and never registered in the app's own container as an
/// <see cref="IClientConnectionStore"/>: nothing in the server should be able to ask for
/// "the client's connection store" and get the keys to every server this device manages.
/// Only the console and the relays it composes read it, each relay through a one-entry
/// view of its own server (<see cref="SingleServerConnectionStore"/>).
/// </para>
/// <para>
/// Reads come from an immutable snapshot rather than the underlying store's live object.
/// That store mutates its cached copy in place under its write gate; with one reader that
/// was fine, but here every relay reads on every request while the console adds, forgets
/// and re-pairs servers — and enumerating a list that another thread is inserting into
/// throws. A snapshot is published after each successful write and never changes again.
/// </para>
/// </remarks>
public sealed class ManagedServerStore : IClientConnectionStore
{
    private static readonly JsonSerializerOptions CloneOptions = new();

    private readonly ClientConnectionStore _inner;
    private readonly Lock _gate = new();
    private ClientConnectionData _snapshot;
    private long _publishedVersion;
    private long _version;

    /// <remarks>
    /// Reads the file here, once, so the first snapshot is taken before any write can be
    /// in flight. Creates nothing: a device that manages no server has no file.
    /// </remarks>
    public ManagedServerStore(IClientDataDirectory directory)
    {
        Directory = directory;
        _inner = new ClientConnectionStore(directory);
        _snapshot = Clone(_inner.Read());
    }

    public IClientDataDirectory Directory { get; }

    /// <summary>
    /// The current contents. Treat as read-only: it is shared with every other reader, and
    /// changes go through <see cref="MutateAsync{T}"/>.
    /// </summary>
    public ClientConnectionData Read()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    /// <summary>One managed server, or null.</summary>
    public ClientServerConnection? Find(string serverId) =>
        Read().Servers.FirstOrDefault(s => string.Equals(s.ServerId, serverId, StringComparison.Ordinal));

    public async Task MutateAsync(Action<ClientConnectionData> mutate, CancellationToken ct = default) =>
        await MutateAsync<object?>(data =>
        {
            mutate(data);
            return null;
        }, ct);

    public async Task<T> MutateAsync<T>(Func<ClientConnectionData, T> mutate, CancellationToken ct = default)
    {
        long version = 0;
        ClientConnectionData? published = null;

        var result = await _inner.MutateAsync(data =>
        {
            var value = mutate(data);

            // Inside the underlying write gate, so versions are handed out in the order
            // the writes happen; the publish below may run out of that order.
            version = Interlocked.Increment(ref _version);
            published = Clone(data);

            return value;
        }, ct);

        lock (_gate)
        {
            if (version > _publishedVersion && published != null)
            {
                _publishedVersion = version;
                _snapshot = published;
            }
        }

        return result;
    }

    /// <summary>
    /// A deep copy by round trip, so a field added to the model later is carried without
    /// anyone having to remember this method exists.
    /// </summary>
    internal static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CloneOptions), CloneOptions)!;
}
