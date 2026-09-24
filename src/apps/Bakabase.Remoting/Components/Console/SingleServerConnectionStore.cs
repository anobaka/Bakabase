using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// One managed server, presented to its relay as if it were the only server there is.
/// </summary>
/// <remarks>
/// <para>
/// The relay core reads "the connection store" and signs with whichever server is active
/// in it — its shape comes from the removed thin client, which had exactly one store.
/// Handing it the whole managed list would put every server's key in reach of every relay;
/// this view is what makes a relay unable to sign for any server but its own, whatever its
/// code does with the store.
/// </para>
/// <para>
/// So reads show that one server, active, or nothing at all once it is forgotten — the
/// relay then answers as a relay with no server, sending a window to the unavailable page
/// and refusing to forward, which is the right reading. And
/// writes are narrowed to the two things a relay legitimately records about its own server:
/// when it last answered, and where its libraries are on this machine. Anything else a
/// mutation does to the projection — another server, a key, which server is active — is
/// dropped rather than written through.
/// </para>
/// </remarks>
public sealed class SingleServerConnectionStore(ManagedServerStore managed, string serverId) : IClientConnectionStore
{
    private readonly Lock _gate = new();
    private ClientConnectionData? _source;
    private ClientConnectionData? _projection;

    public string ServerId => serverId;

    /// <remarks>
    /// Cached per snapshot: the managed store publishes a new, never-changing snapshot on
    /// every write, so one projection per snapshot is exact — and the relay asks on every
    /// request it forwards.
    /// </remarks>
    public ClientConnectionData Read()
    {
        var source = managed.Read();

        lock (_gate)
        {
            if (!ReferenceEquals(source, _source) || _projection == null)
            {
                _projection = Project(source);
                _source = source;
            }

            return _projection;
        }
    }

    public async Task MutateAsync(Action<ClientConnectionData> mutate, CancellationToken ct = default) =>
        await MutateAsync<object?>(data =>
        {
            mutate(data);
            return null;
        }, ct);

    public async Task<T> MutateAsync<T>(Func<ClientConnectionData, T> mutate, CancellationToken ct = default) =>
        await managed.MutateAsync(data =>
        {
            var projection = Project(data);
            var presented = Find(projection.Servers);
            var result = mutate(projection);

            var entry = Find(data.Servers);
            var edited = Find(projection.Servers);

            // Only an edit of the entry it was shown counts. A replacement — a pairing
            // saved through the view — would otherwise carry its empty mapping list over
            // the real one.
            if (entry != null && edited != null && ReferenceEquals(presented, edited))
            {
                entry.LastConnectedAt = edited.LastConnectedAt;
                entry.PathMappings = edited.PathMappings
                    .Select(m => new ClientPathMapping {ServerPath = m.ServerPath, LocalPath = m.LocalPath})
                    .ToList();
            }

            return result;
        }, ct);

    private ClientConnectionData Project(ClientConnectionData data)
    {
        var entry = Find(data.Servers);

        return new ClientConnectionData
        {
            Servers = entry == null ? [] : [ManagedServerStore.Clone(entry)],
            ActiveServerId = entry?.ServerId,
            DeviceName = data.DeviceName,
            Platform = data.Platform
        };
    }

    private ClientServerConnection? Find(IEnumerable<ClientServerConnection> servers) =>
        servers.FirstOrDefault(s => string.Equals(s.ServerId, serverId, StringComparison.Ordinal));
}
