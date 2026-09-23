using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Queries;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using DbReservedValue = Bakabase.Abstractions.Models.Db.ReservedPropertyValue;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// Copies only local title/source data. No full Resource hydration, filesystem scans, metadata fetches,
/// display-name templates or peer calls. Each capture owns its scope and cancellable EF reads.
/// </summary>
public sealed class ServiceLocalLibraryReader(IServiceScopeFactory scopes, INodeIdentityProvider identity,
    TimeProvider? timeProvider = null) : ILocalLibraryReader
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private sealed class FrozenOptions(ResourceOptions value) : IBOptions<ResourceOptions>
    {
        public ResourceOptions Value { get; } = value;
    }

    public async Task<LocalLibraryCapture> CaptureAsync(CaptureBudget budget, CancellationToken cancellationToken)
    {
        var started = _time.GetUtcNow();
        var node = await identity.GetAsync(cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<BakabaseDbContext>();
        var configured = provider.GetRequiredService<IBOptions<ResourceOptions>>().Value.PropertyValueScopePriority;
        // Use the existing resolver unchanged, against a copied configuration for this entire observation.
        var resolver = new PropertyValueScopeResolver(new FrozenOptions(new ResourceOptions
        {
            PropertyValueScopePriority = configured?.ToArray() ?? []
        }));
        var nameId = (int)ReservedProperty.Name;
        var resources = new Dictionary<int, Resource>();
        var sources = new Dictionary<int, HashSet<int>>();

        await foreach (var row in db.Set<ResourceDbModel>().AsNoTracking()
                           .Select(r => new { r.Id, r.Path }).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            budget.Charge(384);
            budget.ChargeString(row.Path);
            resources.Add(row.Id, new Resource { Id = row.Id, Path = row.Path });
        }
        if (resources.Count == 0)
            return new(node.NodeId, node.LibraryEpoch, node.Name, started, _time.GetUtcNow(), []);

        await foreach (var row in db.Set<DbReservedValue>().AsNoTracking()
                           .Where(v => v.Name != null)
                           .Select(v => new { v.ResourceId, v.Scope, v.Name })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            budget.Charge(128);
            budget.ChargeString(row.Name);
            if (!resources.TryGetValue(row.ResourceId, out var resource)) continue;
            resource.Properties ??= new();
            if (!resource.Properties.TryGetValue((int)PropertyPool.Reserved, out var properties))
                resource.Properties[(int)PropertyPool.Reserved] = properties = new();
            if (!properties.TryGetValue(nameId, out var property))
                properties[nameId] = property = new Resource.Property("Name", PropertyType.SingleLineText, []);
            property.Values!.Add(new(row.Scope, row.Name, row.Name, row.Name));
        }

        // Only the reserved Name preference is needed. Stream its existing persisted scope:fallback encoding.
        await foreach (var row in db.Set<PropertyValueScopePreferenceDbModel>().AsNoTracking()
                           .Where(p => p.PropertyPool == PropertyPool.Reserved && p.PropertyId == nameId)
                           .Select(p => new { p.ResourceId, p.Priorities })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            budget.Charge(128);
            budget.ChargeString(row.Priorities);
            if (!resources.TryGetValue(row.ResourceId, out var resource)) continue;
            var priorities = string.IsNullOrEmpty(row.Priorities) ? null : row.Priorities
                .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(entry =>
                {
                    var parts = entry.Split(':');
                    return new PropertyValueScopePriority
                    {
                        Scope = (PropertyValueScope)int.Parse(parts[0]),
                        FallbackOnEmpty = parts.Length > 1 && parts[1] == "1"
                    };
                }).ToArray();
            resource.ScopePreferences = [new PropertyValueScopePreference
            {
                ResourceId = row.ResourceId, PropertyPool = PropertyPool.Reserved,
                PropertyId = nameId, Priorities = priorities
            }];
        }

        // Freeze profile definitions first. Matching profile IDs are read in bounded batches from the existing index.
        var profiles = new Dictionary<int, PropertyValueScope[]?>();
        await foreach (var row in db.Set<ResourceProfileDbModel>().AsNoTracking()
                           .Where(p => p.PropertiesJson != null)
                           .Select(p => new { p.Id, p.PropertiesJson })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            budget.Charge(128);
            // Profile JSON is internal configuration, not a wire title. Charge parsing workspace without truncation.
            budget.Charge(256L + (row.PropertiesJson?.Length ?? 0) * 8L);
            var options = JsonConvert.DeserializeObject<ResourceProfilePropertyOptions>(row.PropertiesJson!);
            if (options == null) continue;
            // A profile with property options wins even when it does not override Name's scope order.
            profiles[row.Id] = options.Properties?
                .FirstOrDefault(p => p.Pool == PropertyPool.Reserved && p.Id == nameId)?.ScopePriority?.ToArray();
        }
        if (profiles.Count > 0)
        {
            var index = provider.GetRequiredService<IResourceProfileIndexService>();
            // Do not pass cancellation into the legacy shared readiness TCS: that would cancel other callers.
            await index.WaitUntilReady().WaitAsync(cancellationToken);
            foreach (var chunk in resources.Keys.Chunk(128))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matches = await index.GetMatchingProfileIdsForResources(chunk).WaitAsync(cancellationToken);
                foreach (var (resourceId, profileIds) in matches)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    budget.Charge(64L + profileIds.Count * 4L);
                    var winning = profileIds.FirstOrDefault(profiles.ContainsKey);
                    if (winning == 0 || profiles[winning] is not { Length: > 0 } priority) continue;
                    var property = resources[resourceId].Properties?
                        .GetValueOrDefault((int)PropertyPool.Reserved)?.GetValueOrDefault(nameId);
                    if (property != null)
                    {
                        budget.Charge(priority.Length * 4L);
                        property.ProfileScopePriority = priority.ToArray();
                    }
                }
            }
        }

        await foreach (var row in db.Set<ResourceSourceLinkDbModel>().AsNoTracking()
                           .Select(s => new { s.ResourceId, s.Source })
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            budget.Charge(48);
            // Rows from removed sources (Bangumi/VNDB betas) stay in older databases.
            if (!resources.ContainsKey(row.ResourceId) || !Enum.IsDefined(row.Source)) continue;
            if (!sources.TryGetValue(row.ResourceId, out var values)) sources[row.ResourceId] = values = [];
            values.Add((int)row.Source);
        }

        var output = new List<LocalResourceProjection>(resources.Count);
        foreach (var resource in resources.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = resolver.Resolve(resource, PropertyPool.Reserved, nameId)?.BizValue as string;
            var filename = string.IsNullOrEmpty(resource.Path) ? null : Path.GetFileName(resource.Path);
            budget.Charge(128);
            budget.ChargeString(name);
            budget.ChargeString(filename);
            output.Add(new(resource.Id, name, filename, resource.HasLocalPath,
                sources.GetValueOrDefault(resource.Id)?.Order().ToArray() ?? []));
        }
        var current = await identity.GetAsync(cancellationToken);
        if (current.NodeId != node.NodeId || current.LibraryEpoch != node.LibraryEpoch)
            throw new FederationQueryException("LibraryEpochChanged", 409);
        return new(node.NodeId, node.LibraryEpoch, node.Name, started, _time.GetUtcNow(), output);
    }
}
