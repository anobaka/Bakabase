using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;

namespace Bakabase.Service.Components.Federation;

public interface IFederationDirectoryOpener
{
    bool Available { get; }
    void Open(string directory, CancellationToken ct = default);
}

public sealed class FederationDirectoryOpener : IFederationDirectoryOpener
{
    public bool Available => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true" &&
        (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ||
         OperatingSystem.IsLinux() && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ||
                                     !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))));

    public void Open(string directory, CancellationToken ct = default)
    {
        if (!Available) throw new FederationQueryException("OpenDirectoryUnavailable", 501);
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
            throw new FederationQueryException("MappedPathUnavailable", 409);
        var start = new ProcessStartInfo { UseShellExecute = false };
        if (OperatingSystem.IsWindows()) start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        else if (OperatingSystem.IsMacOS()) start.FileName = "/usr/bin/open";
        else start.FileName = "xdg-open";
        // Never interpolate resource names into shell text or launch the resource itself.
        start.ArgumentList.Add(directory);
        ct.ThrowIfCancellationRequested();
        try { using var process = Process.Start(start); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { throw new FederationQueryException("OpenDirectoryUnavailable", 501, innerException: ex); }
    }
}

public sealed class FederationDirectoryService(INodeIdentityProvider identity, FederationResourceService local,
    IPeerSessionFactory sessions, INodeTransport transport, FederationPeerService peers, IFederationDirectoryOpener opener,
    GrantLeaseRegistry leases)
{
    public async Task<FederationDirectoryAccess> GetAccessAsync(FederatedResourceDetail detail,
        PeerSessionSnapshot? peer, CancellationToken ct)
    {
        if (!opener.Available) return new(false, "OpenDirectoryUnavailable");
        if (detail.Availability == "MetadataOnly") return new(false, "NoLinkedFile");
        if (peer == null)
        {
            var localLocation = await local.GetLocationAsync(FederationQueryAccess.LocalGrantId, detail.Ref, ct);
            return new(localLocation.LocalPath != null, localLocation.LocalPath == null ? "MappedPathUnavailable" : null);
        }
        var (path, reason) = await MapAsync(detail.Ref.NodeId, detail.Location, ct);
        return new(path != null, reason);
    }

    public async Task<OpenResourceDirectoryResponse> OpenAsync(ResourceRef reference, CancellationToken ct)
    {
        if (reference == null || !NodeRequestSignature.IsIdentifier(reference.NodeId) ||
            !NodeRequestSignature.IsIdentifier(reference.LibraryEpoch) || reference.ResourceId <= 0)
            throw new FederationQueryException("InvalidResourceRefs", 422);
        if (!opener.Available) throw new FederationQueryException("OpenDirectoryUnavailable", 501);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var self = await identity.GetAsync(deadline.Token);
        if (reference.NodeId == self.NodeId)
        {
            var location = await local.GetLocationAsync(FederationQueryAccess.LocalGrantId, reference, deadline.Token);
            if (location.LocalPath == null) throw new FederationQueryException("NoLinkedFile", 409);
            return OpenDirectory(location.LocalPath, location.Response.Location?.IsDirectory == true, deadline.Token);
        }

        var peer = await sessions.GetAsync(reference.NodeId, deadline.Token);
        using var authorized = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            leases.GetCancellationToken(GrantLeaseRegistry.OutboundKey(peer.GrantId)));
        using var response = await transport.SendAsync(peer, HttpMethod.Post,
            "/federation/v1/export/resources/location", new ResourceLocationRequest(reference), authorized.Token);
        var remoteLocation = await FederationMediaService.ReadJsonAsync<ResourceLocationResponse>(response, authorized.Token);
        if (remoteLocation.Ref != reference) throw new FederationQueryException("InvalidPeerResponse", 502);
        var mapped = await MapAsync(reference.NodeId, remoteLocation.Location, authorized.Token);
        if (mapped.Path == null) throw new FederationQueryException(mapped.Reason!, 409);
        return OpenDirectory(mapped.Path, remoteLocation.Location!.IsDirectory, authorized.Token);
    }

    private OpenResourceDirectoryResponse OpenDirectory(string path, bool isDirectory, CancellationToken ct)
    {
        var directory = isDirectory ? path : Path.GetDirectoryName(path);
        ct.ThrowIfCancellationRequested();
        if (directory == null || !Directory.Exists(directory)) throw new FederationQueryException("MappedPathUnavailable", 409);
        ct.ThrowIfCancellationRequested();
        opener.Open(directory, ct);
        return new(true);
    }

    private async Task<(string? Path, string? Reason)> MapAsync(string nodeId, FederatedResourceLocation? location, CancellationToken ct)
    {
        if (location == null) return (null, "MappedPathUnavailable");
        if (!NodeRequestSignature.IsIdentifier(location.SourceRootId) || location.RelativePath is not { Length: > 0 and <= 4096 })
            throw new FederationQueryException("InvalidPeerResponse", 502);
        var mappings = await peers.GetPathMappingsAsync(nodeId, ct);
        var mapping = mappings.FirstOrDefault(m => m.SourceRootId == location.SourceRootId);
        if (mapping == null) return (null, "PathMappingRequired");
        var mapped = MediaPathBoundary.MapLocation(mapping.LocalPath, location.RelativePath, location.IsDirectory);
        return (mapped, mapped == null ? "MappedPathUnavailable" : null);
    }
}
