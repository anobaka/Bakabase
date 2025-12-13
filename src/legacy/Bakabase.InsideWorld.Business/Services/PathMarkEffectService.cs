using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Services;

public class PathMarkEffectService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, ResourceMarkEffectDbModel, int> resourceEffectOrm,
    FullMemoryCacheResourceService<TDbContext, PropertyMarkEffectDbModel, int> propertyEffectOrm,
    IServiceProvider serviceProvider
) : ScopedService(serviceProvider), IPathMarkEffectService where TDbContext : DbContext
{
    #region ResourceMarkEffect Operations

    public async Task<List<ResourceMarkEffect>> GetResourceEffectsByMarkId(int markId)
    {
        var dbModels = await resourceEffectOrm.GetAll(e => e.MarkId == markId);
        return dbModels.ToDomainModels();
    }

    public async Task<List<ResourceMarkEffect>> GetResourceEffectsByMarkIds(IEnumerable<int> markIds)
    {
        var markIdSet = markIds.ToHashSet();
        if (markIdSet.Count == 0) return new List<ResourceMarkEffect>();

        var dbModels = await resourceEffectOrm.GetAll(e => markIdSet.Contains(e.MarkId));
        return dbModels.ToDomainModels();
    }

    public async Task<List<ResourceMarkEffect>> GetResourceEffectsByPath(string path)
    {
        var standardizedPath = path.StandardizePath()!;
        var dbModels = await resourceEffectOrm.GetAll(e => e.Path == standardizedPath);
        return dbModels.ToDomainModels();
    }

    public async Task<List<ResourceMarkEffect>> GetResourceEffectsByPaths(IEnumerable<string> paths)
    {
        var standardizedPaths = paths.Select(p => p.StandardizePath()!).ToHashSet();
        if (standardizedPaths.Count == 0) return new List<ResourceMarkEffect>();

        var dbModels = await resourceEffectOrm.GetAll(e => standardizedPaths.Contains(e.Path));
        return dbModels.ToDomainModels();
    }

    public async Task AddResourceEffects(IEnumerable<ResourceMarkEffect> effects)
    {
        var now = DateTime.UtcNow;
        var dbModels = effects.Select(e =>
        {
            var dbModel = e.ToDbModel();
            dbModel.Path = dbModel.Path.StandardizePath()!;
            dbModel.CreatedAt = now;
            return dbModel;
        }).ToList();

        if (dbModels.Count > 0)
        {
            await resourceEffectOrm.AddRange(dbModels);
        }
    }

    public async Task DeleteResourceEffectsByMarkId(int markId)
    {
        var effects = await resourceEffectOrm.GetAll(e => e.MarkId == markId);
        if (effects.Count > 0)
        {
            await resourceEffectOrm.RemoveRange(effects);
        }
    }

    public async Task DeleteResourceEffects(IEnumerable<int> effectIds)
    {
        var idSet = effectIds.ToHashSet();
        if (idSet.Count == 0) return;

        var effects = await resourceEffectOrm.GetAll(e => idSet.Contains(e.Id));
        if (effects.Count > 0)
        {
            await resourceEffectOrm.RemoveRange(effects);
        }
    }

    #endregion

    #region PropertyMarkEffect Operations

    public async Task<List<PropertyMarkEffect>> GetPropertyEffectsByMarkId(int markId)
    {
        var dbModels = await propertyEffectOrm.GetAll(e => e.MarkId == markId);
        return dbModels.ToDomainModels();
    }

    public async Task<List<PropertyMarkEffect>> GetPropertyEffectsByMarkIds(IEnumerable<int> markIds)
    {
        var markIdSet = markIds.ToHashSet();
        if (markIdSet.Count == 0) return new List<PropertyMarkEffect>();

        var dbModels = await propertyEffectOrm.GetAll(e => markIdSet.Contains(e.MarkId));
        return dbModels.ToDomainModels();
    }

    public async Task<List<PropertyMarkEffect>> GetPropertyEffectsByResource(int resourceId, PropertyPool? pool = null, int? propertyId = null)
    {
        var dbModels = await propertyEffectOrm.GetAll(e =>
            e.ResourceId == resourceId &&
            (pool == null || e.PropertyPool == (int)pool) &&
            (propertyId == null || e.PropertyId == propertyId));
        return dbModels.ToDomainModels();
    }

    public async Task<List<PropertyMarkEffect>> GetPropertyEffectsByResources(IEnumerable<int> resourceIds, PropertyPool? pool = null, int? propertyId = null)
    {
        var resourceIdSet = resourceIds.ToHashSet();
        if (resourceIdSet.Count == 0) return new List<PropertyMarkEffect>();

        var dbModels = await propertyEffectOrm.GetAll(e =>
            resourceIdSet.Contains(e.ResourceId) &&
            (pool == null || e.PropertyPool == (int)pool) &&
            (propertyId == null || e.PropertyId == propertyId));
        return dbModels.ToDomainModels();
    }

    public async Task AddPropertyEffects(IEnumerable<PropertyMarkEffect> effects)
    {
        var now = DateTime.UtcNow;
        var dbModels = effects.Select(e =>
        {
            var dbModel = e.ToDbModel();
            dbModel.CreatedAt = now;
            dbModel.UpdatedAt = now;
            return dbModel;
        }).ToList();

        if (dbModels.Count > 0)
        {
            await propertyEffectOrm.AddRange(dbModels);
        }
    }

    public async Task UpdatePropertyEffects(IEnumerable<PropertyMarkEffect> effects)
    {
        var now = DateTime.UtcNow;
        var dbModels = effects.Select(e =>
        {
            var dbModel = e.ToDbModel();
            dbModel.UpdatedAt = now;
            return dbModel;
        }).ToList();

        if (dbModels.Count > 0)
        {
            await propertyEffectOrm.UpdateRange(dbModels);
        }
    }

    public async Task DeletePropertyEffectsByMarkId(int markId)
    {
        var effects = await propertyEffectOrm.GetAll(e => e.MarkId == markId);
        if (effects.Count > 0)
        {
            await propertyEffectOrm.RemoveRange(effects);
        }
    }

    public async Task DeletePropertyEffects(IEnumerable<int> effectIds)
    {
        var idSet = effectIds.ToHashSet();
        if (idSet.Count == 0) return;

        var effects = await propertyEffectOrm.GetAll(e => idSet.Contains(e.Id));
        if (effects.Count > 0)
        {
            await propertyEffectOrm.RemoveRange(effects);
        }
    }

    #endregion

    #region Value Calculation

    public async Task<List<string>> GetAggregatedMultiSelectValue(int resourceId, PropertyPool pool, int propertyId)
    {
        var effects = await GetPropertyEffectsByResource(resourceId, pool, propertyId);
        var allValues = new HashSet<string>();

        foreach (var effect in effects)
        {
            if (!string.IsNullOrEmpty(effect.Value))
            {
                // Value is comma-separated list
                var values = effect.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var v in values)
                {
                    allValues.Add(v.Trim());
                }
            }
        }

        return allValues.ToList();
    }

    public async Task<(string? Value, int? WinningMarkId)> GetWinningSingleSelectValue(int resourceId, PropertyPool pool, int propertyId)
    {
        var effects = await GetPropertyEffectsByResource(resourceId, pool, propertyId);

        if (effects.Count == 0)
            return (null, null);

        // Get the effect with highest priority (then by MarkId descending for stable ordering)
        var winner = effects.OrderByDescending(e => e.Priority).ThenByDescending(e => e.MarkId).First();
        return (winner.Value, winner.MarkId);
    }

    #endregion
}
