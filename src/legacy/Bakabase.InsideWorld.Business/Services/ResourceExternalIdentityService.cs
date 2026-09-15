using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Services;

public class ResourceExternalIdentityService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, ResourceExternalIdentityDbModel, int> orm)
    : IResourceExternalIdentityService where TDbContext : DbContext
{
    private static readonly SemaphoreSlim EnsureLock = new(1, 1);

    public async Task<List<ResourceExternalIdentity>> GetByResourceId(int resourceId) =>
        (await orm.GetAll(x => x.ResourceId == resourceId)).Select(x => x.ToDomainModel()).ToList();

    public async Task<Dictionary<int, List<ResourceExternalIdentity>>> GetByResourceIdsGrouped(int[] resourceIds)
    {
        var ids = resourceIds.ToHashSet();
        return (await orm.GetAll(x => ids.Contains(x.ResourceId)))
            .Select(x => x.ToDomainModel())
            .GroupBy(x => x.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<int?> FindResource(ThirdPartyId thirdPartyId, string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return null;
        var normalizedId = externalId.Trim();
        return (await orm.GetAll(x => x.ThirdPartyId == thirdPartyId && x.ExternalId == normalizedId))
            .OrderBy(x => x.Id)
            .Select(x => (int?) x.ResourceId)
            .FirstOrDefault();
    }

    public async Task<List<int>> FindConflictingResourceIds(int resourceId)
    {
        var identities = (await orm.GetAll(x => x.ResourceId == resourceId))
            .Select(x => (x.ThirdPartyId, x.ExternalId)).ToHashSet();
        if (identities.Count == 0) return [];
        return (await orm.GetAll(x => x.ResourceId != resourceId))
            .Where(x => identities.Contains((x.ThirdPartyId, x.ExternalId)))
            .Select(x => x.ResourceId).Distinct().ToList();
    }

    public async Task EnsureIdentities(int resourceId, IEnumerable<ResourceExternalIdentity> identities)
    {
        await EnsureLock.WaitAsync();
        try
        {
            var existing = await orm.GetAll(x => x.ResourceId == resourceId);
            var byIdentity = existing.ToDictionary(x => (x.ThirdPartyId, x.ExternalId), x => x.ToDomainModel());
            foreach (var identity in identities)
            {
                if (string.IsNullOrWhiteSpace(identity.ExternalId))
                    throw new ArgumentException("An external identity requires an ID.", nameof(identities));

                var externalId = identity.ExternalId.Trim();
                if (!byIdentity.TryGetValue((identity.ThirdPartyId, externalId), out var target))
                {
                    target = new ResourceExternalIdentity
                    {
                        ResourceId = resourceId,
                        ThirdPartyId = identity.ThirdPartyId,
                        ExternalId = externalId,
                        CreateDt = identity.CreateDt == default ? DateTime.UtcNow : identity.CreateDt
                    };
                    byIdentity.Add((target.ThirdPartyId, target.ExternalId), target);
                }

                var coverUrls = MergeValues(target.CoverUrls, identity.CoverUrls);
                if (coverUrls?.Count > (target.CoverUrls?.Count ?? 0)) target.CoverDownloadFailedAt = null;
                target.CoverUrls = coverUrls;
                target.MetadataJson ??= identity.MetadataJson;

                // A merged-away resource's cache directory is deleted with that resource.
                // Preserve its known URLs so the surviving resource can download its own copy.
                if (identity.ResourceId == 0 || identity.ResourceId == resourceId)
                    target.LocalCoverPaths = MergeValues(target.LocalCoverPaths, identity.LocalCoverPaths);
            }

            var models = byIdentity.Values.Select(x => x.ToDbModel()).ToList();
            var added = models.Where(x => x.Id == 0).ToList();
            var originals = existing.ToDictionary(x => x.Id);
            var changed = models.Where(x => x.Id != 0 && x != originals[x.Id]).ToList();
            if (added.Count > 0) await orm.AddRange(added);
            if (changed.Count > 0) await orm.UpdateRange(changed);
        }
        finally
        {
            EnsureLock.Release();
        }
    }

    public async Task DeleteByResourceIds(IEnumerable<int> resourceIds)
    {
        var ids = resourceIds.ToHashSet();
        var models = await orm.GetAll(x => ids.Contains(x.ResourceId));
        if (models.Count > 0) await orm.RemoveRange(models);
    }

    public async Task Update(ResourceExternalIdentity identity) => await orm.Update(identity.ToDbModel());

    public async Task ClearLocalCoverPaths(int resourceId)
    {
        var models = await orm.GetAll(x => x.ResourceId == resourceId);
        foreach (var model in models)
        {
            model.LocalCoverPaths = null;
            model.CoverDownloadFailedAt = null;
        }

        if (models.Count > 0) await orm.UpdateRange(models);
    }

    private static List<string>? MergeValues(List<string>? current, List<string>? incoming)
    {
        var values = (current ?? []).Concat(incoming ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
        return values.Count == 0 ? null : values;
    }
}
