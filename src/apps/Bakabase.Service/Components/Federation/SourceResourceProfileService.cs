using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Models.View;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bootstrap.Models.ResponseModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// Device-local profile configuration, source-owned matching. The source receives only bounded searches and IDs.
/// Management transport is an optional host abstraction; the Service never references a desktop relay.
/// </summary>
public sealed class SourceResourceProfileService(IResourceProfileService profiles,
    IManagedServerService? managed = null, MetadataReplicaRegistry? metadata = null) : ISourceResourceProfilePlaybackService
{
    private static readonly JsonSerializerSettings Json = new()
        { ContractResolver = new CamelCasePropertyNamesContractResolver(), MaxDepth = 64 };

    public async Task<List<ResourceProfileSourceViewModel>> SourcesAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, ResourceProfileSourceViewModel>(StringComparer.Ordinal);
        if (managed != null)
        {
            var devices = await managed.GetAsync(false, ct);
            var identities = metadata == null ? null : (await metadata.ReadAsync(ct)).ToDictionary(r => r.ServerId);
            foreach (var device in devices.Servers)
            {
                var known = identities?.GetValueOrDefault(device.ServerId);
                // Only the validated metadata handshake supplies the library incarnation.
                // A cached connection name or merely knowing an address cannot authorize a rule.
                var verified = known != null && !known.Denied &&
                    NodeRequestSignature.IsIdentifier(known.LibraryEpoch) &&
                    device.State is not (ManagedServerState.Revoked or ManagedServerState.WrongServer);
                result[device.ServerId] = new ResourceProfileSourceViewModel(device.ServerId,
                    known?.Name ?? device.Name ?? device.ServerId, verified ? known!.LibraryEpoch : null,
                    verified && device.State == ManagedServerState.Online, "managed");
            }
        }
        return result.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<ResourceProfileSourceInfo> InfoAsync(string deviceId, string? libraryEpoch, CancellationToken ct)
    {
        var result = await ReadAsync<SingletonResponse<ResourceProfileSourceInfo>>(deviceId, "identity", null,
            libraryEpoch, ct);
        var info = result.Data;
        if (info == null || info.DeviceId != deviceId || !NodeRequestSignature.IsIdentifier(info.LibraryEpoch) ||
            info.Name is not {Length: > 0 and <= 1024})
            throw new FederationQueryException("InvalidProfileSource", 502);
        if (libraryEpoch != null && info.LibraryEpoch != libraryEpoch)
            throw new FederationQueryException("LibraryEpochChanged", 409);
        return info;
    }

    public async Task<T> ReadAsync<T>(string deviceId, string operation, object? input, string? libraryEpoch,
        CancellationToken ct) where T : BaseResponse
    {
        if (!NodeRequestSignature.IsIdentifier(deviceId) ||
            libraryEpoch != null && !NodeRequestSignature.IsIdentifier(libraryEpoch))
            throw new FederationQueryException("InvalidProfileSource", 422);
        if (operation is not ("identity" or "properties" or "filter-value" or "search-resolve" or "preview" or "evaluate"))
            throw new ArgumentOutOfRangeException(nameof(operation));
        var json = input == null ? null : JsonConvert.SerializeObject(input, Json);
        if (json != null && Encoding.UTF8.GetByteCount(json) > NodeRequestSignature.MaxControlBodyBytes)
            throw new FederationQueryException("ProfileSearchTooLarge", 413);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        string response;
        var servers = managed == null ? null : await managed.GetAsync(false, deadline.Token);
        if (servers?.Servers.Any(s => s.ServerId == deviceId) == true)
        {
            response = await managed!.ReadResourceProfileSourceAsync(deviceId, operation, json, libraryEpoch,
                deadline.Token);
        }
        else throw new FederationQueryException("ManagedServerNotFound", 404,
            "Add this device through management before configuring its resource rules.");
        if (Encoding.UTF8.GetByteCount(response) > ResourceProfileSourceReader.MaximumResponseBytes)
            throw new FederationQueryException("ProfileMetadataTooLarge", 502);
        T result;
        try { result = JsonConvert.DeserializeObject<T>(response, Json) ?? throw new JsonSerializationException(); }
        catch (Newtonsoft.Json.JsonException e)
        { throw new FederationQueryException("InvalidProfileSource", 502, innerException: e); }
        if (result.Code != 0)
            throw new FederationQueryException("ProfileSearchFailed", 422,
                "The source could not evaluate this resource profile. Check its filters and availability.");
        deadline.Token.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<ResourceProfileOpeningOptions> ResolveAsync(string sourceDeviceId, string? libraryEpoch,
        int resourceId, CancellationToken cancellationToken = default) =>
        (await ResolveManyAsync(sourceDeviceId, libraryEpoch, [resourceId], cancellationToken))[resourceId];

    public async Task<IReadOnlyDictionary<int, ResourceProfileOpeningOptions>> ResolveManyAsync(string sourceDeviceId,
        string? libraryEpoch, IReadOnlyList<int> resourceIds, CancellationToken cancellationToken = default)
    {
        if (!NodeRequestSignature.IsIdentifier(sourceDeviceId) || resourceIds.Count > 256 || resourceIds.Any(id => id <= 0))
            throw new FederationQueryException("InvalidProfileResources", 422);
        var ids = resourceIds.Distinct().ToArray();
        var result = ids.ToDictionary(id => id, _ => new ResourceProfileOpeningOptions());
        // No network is needed when the viewer has not configured a source-specific profile.
        var candidates = await profiles.GetAll(p => p.SourceDeviceId == sourceDeviceId);
        if (candidates.Count == 0 || ids.Length == 0) return result;
        var source = await InfoAsync(sourceDeviceId, libraryEpoch, cancellationToken);
        if (candidates.Any(p => p.SourceLibraryEpoch != source.LibraryEpoch))
            throw new FederationQueryException("ProfileSourceLibraryChanged", 409,
                "The source library identity is missing or has changed. Review this device's resource profile filters before opening its resources.");
        if (candidates.Count > 128) throw new FederationQueryException("TooManySourceProfiles", 422);
        var input = new ResourceProfileEvaluationInput
        {
            ResourceIds = ids,
            Profiles = candidates.Select(p => new ResourceProfileSearchRule { Id = p.Id, SearchJson = p.SearchJson }).ToArray()
        };
        var evaluated = (await ReadAsync<SingletonResponse<ResourceProfileEvaluationResult>>(sourceDeviceId,
            "evaluate", input, source.LibraryEpoch, cancellationToken)).Data;
        ValidateMatches(evaluated, input);
        var byId = candidates.ToDictionary(p => p.Id);
        foreach (var match in evaluated!.Matches)
            result[match.ResourceId] = match.ProfileIds.Select(id => byId[id]).ResolveOpeningOptions();
        return result;
    }

    internal static void ValidateMatches(ResourceProfileEvaluationResult? result, ResourceProfileEvaluationInput input)
    {
        var allowedResources = input.ResourceIds.ToHashSet();
        var allowedProfiles = input.Profiles.Select(p => p.Id).ToHashSet();
        if (result?.Matches == null || result.Matches.Length > allowedResources.Count ||
            result.Matches.Any(m => m == null || !allowedResources.Contains(m.ResourceId) || m.ProfileIds == null ||
                m.ProfileIds.Length > allowedProfiles.Count || m.ProfileIds.Distinct().Count() != m.ProfileIds.Length ||
                m.ProfileIds.Any(id => !allowedProfiles.Contains(id))) ||
            result.Matches.Select(m => m.ResourceId).Distinct().Count() != result.Matches.Length)
            throw new FederationQueryException("InvalidProfileMatches", 502);
        if (result.Matches.Length != allowedResources.Count)
            throw new FederationQueryException("ResourceGone", 410,
                "Some resources no longer exist in the source library. Refresh the resource list before opening them.");
    }
}
