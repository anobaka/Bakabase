using Bakabase.Remoting.Abstractions.Models;

namespace Bakabase.Remoting.Components.Connection;

/// <summary>Where requests go, or null when this client is not pointed at anything yet.</summary>
public interface IUpstreamTarget
{
    string? BaseAddress { get; }
}

/// <summary>
/// The server this client is currently using, and the one place that changes.
/// </summary>
/// <remarks>
/// Reads come off the store's in-memory copy, because the forwarding layer asks for the
/// address and the key on every single request. Writes go through the store, so the two
/// never disagree about which server is active.
/// </remarks>
public sealed class ActiveConnection(IClientConnectionStore store) : IUpstreamTarget, IClientCredentialProvider
{
    public ClientServerConnection? Server
    {
        get
        {
            var data = store.Read();

            if (data.ActiveServerId == null)
            {
                return null;
            }

            return data.Servers.FirstOrDefault(s =>
                string.Equals(s.ServerId, data.ActiveServerId, StringComparison.Ordinal));
        }
    }

    public string? BaseAddress => Server?.BaseAddress;

    public ClientCredentials? Current =>
        Server is {DeviceId: not null, DeviceKey: not null} server
            ? new ClientCredentials(server.DeviceId, server.DeviceKey)
            : null;

    /// <summary>
    /// Records a server this client has paired with and makes it the active one.
    /// Replaces any entry for the same install rather than accumulating duplicates: a
    /// server that moved to a new address is the same server.
    /// </summary>
    public async Task<ClientServerConnection> SaveAsync(string serverId, string? serverName, string baseAddress,
        ClientCredentials credentials, DateTime pairedAt, CancellationToken ct = default) =>
        await store.MutateAsync(data =>
        {
            data.Servers.RemoveAll(s => string.Equals(s.ServerId, serverId, StringComparison.Ordinal));

            var entry = new ClientServerConnection
            {
                ServerId = serverId,
                ServerName = serverName,
                BaseAddress = ServerConnector.Normalize(baseAddress),
                DeviceId = credentials.DeviceId,
                DeviceKey = credentials.Key,
                PairedAt = pairedAt
            };

            data.Servers.Insert(0, entry);
            data.ActiveServerId = serverId;
            return entry;
        }, ct);

    /// <summary>
    /// Points the client at a server it already knows. Does nothing for an id it has no
    /// credentials for — switching to a server that cannot be reached would only make
    /// the client look broken.
    /// </summary>
    public async Task<bool> ActivateAsync(string serverId, CancellationToken ct = default) =>
        await store.MutateAsync(data =>
        {
            if (data.Servers.All(s => !string.Equals(s.ServerId, serverId, StringComparison.Ordinal)))
            {
                return false;
            }

            data.ActiveServerId = serverId;
            return true;
        }, ct);

    /// <summary>
    /// Drops a server and its key. If it was the active one the client is left pointed
    /// at nothing rather than silently moving to another: which server it talks to is
    /// the user's decision, not a fallback.
    /// </summary>
    public async Task<bool> ForgetAsync(string serverId, CancellationToken ct = default) =>
        await store.MutateAsync(data =>
        {
            if (data.Servers.RemoveAll(s => string.Equals(s.ServerId, serverId, StringComparison.Ordinal)) == 0)
            {
                return false;
            }

            if (string.Equals(data.ActiveServerId, serverId, StringComparison.Ordinal))
            {
                data.ActiveServerId = null;
            }

            return true;
        }, ct);

    /// <summary>
    /// Replaces where one server's libraries are on this machine.
    /// </summary>
    /// <remarks>
    /// Per server, and set as a whole rather than appended to, because the settings page
    /// edits the list as a table: a save that merged would leave a row the user deleted
    /// still mapping.
    /// </remarks>
    public async Task<bool> SetPathMappingsAsync(string serverId, IEnumerable<ClientPathMapping> mappings,
        CancellationToken ct = default) =>
        await store.MutateAsync(data =>
        {
            var server = data.Servers.FirstOrDefault(s =>
                string.Equals(s.ServerId, serverId, StringComparison.Ordinal));

            if (server == null)
            {
                return false;
            }

            server.PathMappings = mappings
                .Where(m => !string.IsNullOrWhiteSpace(m.ServerPath) && !string.IsNullOrWhiteSpace(m.LocalPath))
                .ToList();

            return true;
        }, ct);

    /// <summary>
    /// Notes that the server answered. Written at most once every
    /// <see cref="LastConnectedPersistenceInterval"/>, so an active session does not
    /// rewrite the file on every request.
    /// </summary>
    public async Task TouchAsync(DateTime now, CancellationToken ct = default)
    {
        var server = Server;

        if (server == null ||
            (server.LastConnectedAt.HasValue &&
             now - server.LastConnectedAt.Value < LastConnectedPersistenceInterval))
        {
            return;
        }

        await store.MutateAsync(data =>
        {
            var target = data.Servers.FirstOrDefault(s =>
                string.Equals(s.ServerId, server.ServerId, StringComparison.Ordinal));

            if (target != null)
            {
                target.LastConnectedAt = now;
            }
        }, ct);
    }

    public static readonly TimeSpan LastConnectedPersistenceInterval = TimeSpan.FromMinutes(10);
}
