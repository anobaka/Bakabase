using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Queries;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Federation;

/// <summary>Local resource projection only. It never calls acquisition, discovery, workflow, or a remote library.</summary>
public sealed class FederationResourceService(IResourceService resources, IPropertyValueScopeResolver scopes,
    INodeIdentityProvider identity, IFederationQueryAccess access, AssetLeaseStore leases, BakabaseDbContext db)
{
    public const int MaximumMappingRoots = 4096;
    public const int MaximumMappingResourceRows = 250_000;
    public const int MaximumMappingPathChars = 4096;
    public const long MaximumMappingScanBytes = 32L * 1024 * 1024;
    public const int MaximumMappingResponseBytes = 1024 * 1024;
    public static readonly TimeSpan MappingRootDeadline = TimeSpan.FromSeconds(4);

    public async Task<ResourceResolveResponse> ResolveAsync(string grantId, ResourceResolveRequest request,
        CancellationToken ct)
    {
        if (request.Refs is not { Length: > 0 and <= 32 })
            throw new FederationQueryException("InvalidResourceRefs", 422, "Provide between 1 and 32 resource references.");
        var node = await identity.GetAsync(ct);
        var results = new List<FederatedResourceDetail>();
        foreach (var reference in request.Refs)
        {
            ct.ThrowIfCancellationRequested();
            ValidateOwner(reference, node.NodeId, node.LibraryEpoch);
            var permission = await access.ValidateAsync(grantId, reference.LibraryEpoch, ct);
            var resource = await resources.Get(reference.ResourceId,
                ResourceAdditionalItem.Properties | ResourceAdditionalItem.Alias | ResourceAdditionalItem.CollectionName);
            if (resource == null) throw new FederationQueryException("ResourceGone", 410);
            var properties = new List<FederatedProperty>();
            foreach (var (pool, items) in resource.Properties ?? [])
            foreach (var (id, property) in items)
            {
                if (property.Type == PropertyType.Attachment ||
                    pool == (int)PropertyPool.Reserved && id == (int)ReservedProperty.Cover) continue;
                var value = scopes.Resolve(resource, (PropertyPool)pool, id);
                if (value?.AliasAppliedBizValue == null) continue;
                properties.Add(new FederatedProperty(property.Name ?? property.Type.ToString(),
                    property.Type.ToString(), value.AliasAppliedBizValue, value.Scope));
            }

            var candidates = GetCandidates(resource, ct, out var unavailableReason);
            var assets = candidates.Select(candidate =>
            {
                var lease = leases.Issue(reference, grantId, permission.GrantVersion, candidate.Path,
                    candidate.ContentType, candidate.Kind);
                return new FederatedAsset(lease.AssetId, lease.Kind, Path.GetFileName(candidate.Path),
                    lease.ContentType, candidate.Root == null ? null : RootId(candidate.Root),
                    candidate.Root == null ? null : Path.GetRelativePath(candidate.Root, candidate.Path).Replace('\\', '/'),
                    lease.ExpiresAt);
            }).ToArray();
            var name = scopes.Resolve(resource, PropertyPool.Reserved, (int)ReservedProperty.Name)
                ?.AliasAppliedBizValue as string;
            results.Add(new FederatedResourceDetail(reference, node.Name,
                !string.IsNullOrWhiteSpace(name) ? name : resource.FileName ?? $"#{resource.Id}", resource.FileName,
                resource.HasLocalPath ? "HasFile" : "MetadataOnly", properties.ToArray(),
                resource.SourceLinks?.Select(x => new FederatedSource((int)x.Source, x.Source.ToString())).ToArray() ?? [],
                resource.ExternalIdentities?.Select(x => new FederatedExternalIdentity(x.ThirdPartyId.ToString(), x.ExternalId)).ToArray() ?? [],
                resource.Collections?.Select(x => new FederatedCollection(x.Name, x.Color)).ToArray() ?? [],
                assets, unavailableReason));
        }
        return new ResourceResolveResponse(results.ToArray());
    }

    public async Task<AssetLease> OpenAsync(string grantId, string assetId, CancellationToken ct)
    {
        var lease = leases.Get(assetId, grantId);
        var node = await identity.GetAsync(ct);
        ValidateOwner(lease.ResourceRef, node.NodeId, node.LibraryEpoch);
        var permission = await access.ValidateAsync(grantId, lease.ResourceRef.LibraryEpoch, ct);
        if (permission.GrantVersion != lease.GrantVersion)
            throw new FederationQueryException("GrantRevoked", 403);
        var resource = await resources.Get(lease.ResourceRef.ResourceId, ResourceAdditionalItem.Properties);
        if (resource == null || !GetCandidates(resource, ct, out _).Any(x => x.Path == lease.Path))
            throw new FederationQueryException("ResourceGone", 410, "The asset is no longer attached to this resource.");
        return lease;
    }

    public async Task<MappingRoot[]> GetMappingRootsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(MappingRootDeadline);
        var started = Stopwatch.StartNew();
        var roots = new Dictionary<string, MappingRoot>(StringComparer.Ordinal);
        var rowCount = 0;
        long scannedBytes = 0;
        var responseBytes = 2; // JSON array brackets.
        try
        {
            // Stream two scalar columns, not hydrated resources. The SQL projection
            // caps a single malformed database field before allocating its full value.
            await foreach (var row in db.Set<ResourceDbModel>().AsNoTracking()
                               .Where(r => r.Path != null && r.Path != "")
                               .OrderBy(r => r.Id)
                               .Select(r => new
                               {
                                   r.IsFile,
                                   Path = r.Path!.Substring(0, MaximumMappingPathChars + 1)
                               }).Take(MaximumMappingResourceRows + 1)
                               .AsAsyncEnumerable().WithCancellation(deadline.Token))
            {
                deadline.Token.ThrowIfCancellationRequested();
                scannedBytes += 64L + row.Path.Length * 2L;
                if (++rowCount > MaximumMappingResourceRows || scannedBytes > MaximumMappingScanBytes ||
                    started.Elapsed > MappingRootDeadline)
                    throw MappingScanExceeded();
                if (row.Path.Length > MaximumMappingPathChars)
                    throw new FederationQueryException("MappingRootMetadataTooLarge", 503,
                        "A source path exceeds the mapping metadata budget.");
                var root = row.IsFile ? Path.GetDirectoryName(row.Path) : row.Path;
                if (root == null) continue;
                var item = new MappingRoot(RootId(root), Path.GetFileName(root));
                if (roots.ContainsKey(item.SourceRootId)) continue;
                // Measure escaped JSON bytes: names containing non-ASCII characters
                // can be much larger on the wire than their string lengths suggest.
                responseBytes += System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(item, FederationJson.Options).Length + 1;
                if (roots.Count >= MaximumMappingRoots || responseBytes > MaximumMappingResponseBytes)
                    throw new FederationQueryException("MappingRootsTooLarge", 503,
                        "The library's mapping roots exceed the result budget. No partial root list was returned.");
                roots.Add(item.SourceRootId, item);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw MappingScanExceeded();
        }
        ct.ThrowIfCancellationRequested();
        return roots.Values.OrderBy(root => root.Name, StringComparer.Ordinal).ThenBy(root => root.SourceRootId,
            StringComparer.Ordinal).ToArray();
    }

    private static FederationQueryException MappingScanExceeded() => new("ScanBudgetExceeded", 503,
        "The mapping-root scan exceeded its work budget. No partial root list was returned.");

    private List<Candidate> GetCandidates(Resource resource, CancellationToken ct, out string? reason)
    {
        var result = new List<Candidate>();
        reason = resource.HasLocalPath ? "NoSupportedMedia" : "MetadataOnly";
        if (resource.Path is { Length: > 0 } path)
        {
            if (File.Exists(path)) Add(path, Path.GetDirectoryName(path));
            else if (Directory.Exists(path))
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = false, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                var queue = new Queue<(string Path, int Depth)>();
                queue.Enqueue((path, 0));
                var visited = 0;
                var started = Stopwatch.StartNew();
                while (queue.TryDequeue(out var directory))
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Path, "*", options))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (++visited > 4096 || result.Count >= 256 || started.Elapsed > TimeSpan.FromSeconds(2))
                        {
                            reason = "MediaListingLimited";
                            queue.Clear();
                            break;
                        }
                        if (!MediaPathBoundary.IsWithin(path, entry)) continue;
                        if (Directory.Exists(entry))
                        {
                            if (directory.Depth < 16) queue.Enqueue((entry, directory.Depth + 1));
                            else reason = "MediaListingLimited";
                        }
                        else Add(entry, path);
                    }
                }
            }
            else reason = "FileMissing";
        }

        var covers = scopes.Resolve(resource, PropertyPool.Reserved, (int)ReservedProperty.Cover)
            ?.AliasAppliedBizValue as IEnumerable<string>;
        covers ??= resource.SourceLinks?.SelectMany(x => x.LocalCoverPaths ?? [])
            .Concat(resource.ExternalIdentities?.SelectMany(x => x.LocalCoverPaths ?? []) ?? []);
        foreach (var cover in (covers ?? []).Take(32))
        {
            ct.ThrowIfCancellationRequested();
            if (MediaContentTypes.Get(cover)?.Kind == "image" && File.Exists(cover)) Add(cover, null);
        }
        if (result.Count > 0 && reason != "MediaListingLimited") reason = null;
        return result.DistinctBy(x => x.Path).ToList();

        void Add(string file, string? root)
        {
            var mediaType = MediaContentTypes.Get(file);
            if (mediaType == null) return;
            result.Add(new Candidate(Path.GetFullPath(file), root == null ? null : Path.GetFullPath(root),
                mediaType.Value.Kind, mediaType.Value.ContentType));
        }
    }

    private static string RootId(string root) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root)))).ToLowerInvariant();

    private static void ValidateOwner(ResourceRef reference, string nodeId, string epoch)
    {
        if (reference.NodeId != nodeId || reference.LibraryEpoch != epoch || reference.ResourceId <= 0)
            throw new FederationQueryException("LibraryEpochChanged", 409, "Refresh this library before opening the resource.");
    }

    private sealed record Candidate(string Path, string? Root, string Kind, string ContentType);
}
