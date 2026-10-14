using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Newtonsoft.Json;

namespace Bakabase.Service.Components.Federation;

public sealed record ManagedLibraryInfo(string ServerId, string Name, string LibraryEpoch, int ContractVersion = 1);
public sealed record ManagedLibrarySource(string ServerId, string? Name, string? LibraryEpoch, bool IsLocal,
    bool Available, string? ErrorCode = null, string? Error = null, int ContractVersion = 1);
public sealed record ManagedLibrarySources(IReadOnlyList<ManagedLibrarySource> Sources);

/// <summary>No replicas or merged integer IDs: every request remains bound to its selected owner and library incarnation.</summary>
public sealed class ManagedLibraryService(IManagedServerService managed, INodeIdentityProvider identity)
{
    public async Task<ManagedLibraryInfo> InfoAsync(CancellationToken ct)
    {
        var self = await identity.GetAsync(ct);
        return new ManagedLibraryInfo(self.NodeId, self.Name, self.LibraryEpoch);
    }

    public async Task<ManagedLibrarySources> SourcesAsync(CancellationToken ct)
    {
        var self = await InfoAsync(ct);
        var connections = await managed.GetAsync(false, ct);
        using var limit = new SemaphoreSlim(4);
        var remote = await Task.WhenAll(connections.Servers.Select(async server =>
        {
            await limit.WaitAsync(ct);
            try
            {
                var info = await RemoteInfoAsync(server.ServerId, ct);
                return new ManagedLibrarySource(info.ServerId, info.Name, info.LibraryEpoch, false, true);
            }
            catch (FederationAccessException ex)
            {
                return new ManagedLibrarySource(server.ServerId, server.Name, null, false, false, ex.ErrorCode, ex.Message);
            }
            finally { limit.Release(); }
        }));
        return new ManagedLibrarySources(new[] {new ManagedLibrarySource(self.ServerId, self.Name,
            self.LibraryEpoch, true, true)}.Concat(remote).ToArray());
    }

    public async Task<ManagedServerResponse> ExecuteAsync(string serverId, ManagedLibraryRequest request, CancellationToken ct)
    {
        // Validate the named operation before any network request. There is no retry of a write:
        // a lost response does not establish whether the owner applied it.
        var operation = ManagedLibraryOperations.Build(request);
        await RequireSourceAsync(serverId, request.LibraryEpoch, ct);
        return await SendAsync(serverId, operation.Method, operation.Path, operation.Json, ct);
    }

    public async Task RequireSourceAsync(string serverId, string libraryEpoch, CancellationToken ct)
    {
        if (!NodeRequestSignature.IsIdentifier(serverId)) throw new FederationAccessException("InvalidServer", 400, "A source device identity is required.");
        if (!NodeRequestSignature.IsIdentifier(libraryEpoch)) throw new FederationAccessException("LibraryEpochChanged", 409, "A source library identity is required.");
        var self = await identity.GetAsync(ct);
        if (self.NodeId == serverId) throw new FederationAccessException("LocalSource", 400,
            "Use the ordinary local library API for this device.");
        var info = await RemoteInfoAsync(serverId, ct);
        if (info.LibraryEpoch != libraryEpoch) throw new FederationAccessException("LibraryEpochChanged", 409,
            "This device's library was replaced. Refresh its resources before making changes.");
    }

    private async Task<ManagedLibraryInfo> RemoteInfoAsync(string serverId, CancellationToken ct)
    {
        var response = await SendAsync(serverId, HttpMethod.Get, "/management/library/info", null, ct);
        if (response.StatusCode == 404) throw new FederationAccessException("ManagementUpdateRequired", 409,
            "Update this device to use unified library management.");
        if (response.StatusCode is < 200 or >= 300) throw new FederationAccessException("ManagedSourceUnavailable", 503,
            "This device did not allow library access. Check its connection and pairing.");
        ManagedLibraryInfo? info;
        try { info = JsonConvert.DeserializeObject<ManagedLibraryInfo>(response.Body); }
        catch (JsonException) { info = null; }
        if (info?.ContractVersion != 1 || !NodeRequestSignature.IsIdentifier(info.LibraryEpoch))
            throw new FederationAccessException("ManagementUpdateRequired", 409, "The device's management contract is not compatible.");
        if (info.ServerId != serverId) throw new FederationAccessException("ManagedSourceChanged", 409,
            "A different device answered at this address. Check the connection before continuing.");
        return info;
    }

    private async Task<ManagedServerResponse> SendAsync(string serverId, HttpMethod method, string path, string? json,
        CancellationToken ct)
    {
        try { return await managed.SendAsync(serverId, method, path, json, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new FederationAccessException("ManagedSourceUnavailable", 504, "The device did not answer in time. Refresh before retrying a change."); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or NotSupportedException or System.IO.IOException)
        { throw new FederationAccessException("ManagedSourceUnavailable", 503, "The device is unavailable or its management connection changed. Refresh before retrying a change."); }
    }
}
