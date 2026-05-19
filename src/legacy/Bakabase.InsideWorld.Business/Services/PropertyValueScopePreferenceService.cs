using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services;

public class PropertyValueScopePreferenceService(
    FullMemoryCacheResourceService<BakabaseDbContext, PropertyValueScopePreferenceDbModel, int> orm)
    : IPropertyValueScopePreferenceService
{
    public async Task<PropertyValueScopePreference?> Get(int resourceId, PropertyPool pool, int propertyId)
    {
        var row = (await orm.GetAll(x =>
            x.ResourceId == resourceId && x.PropertyPool == pool && x.PropertyId == propertyId)).FirstOrDefault();
        return row == null ? null : ToDomain(row);
    }

    public async Task<List<PropertyValueScopePreference>> GetByResourceIds(IEnumerable<int> resourceIds)
    {
        var ids = resourceIds as HashSet<int> ?? resourceIds.ToHashSet();
        if (ids.Count == 0) return [];
        var rows = await orm.GetAll(x => ids.Contains(x.ResourceId));
        return rows.Select(ToDomain).ToList();
    }

    public async Task<PropertyValueScopePreference> Upsert(PropertyValueScopePreference preference)
    {
        var existing = (await orm.GetAll(x =>
            x.ResourceId == preference.ResourceId &&
            x.PropertyPool == preference.PropertyPool &&
            x.PropertyId == preference.PropertyId)).FirstOrDefault();

        var serialized = SerializePriorities(preference.Priorities);

        if (existing == null)
        {
            var inserted = await orm.Add(new PropertyValueScopePreferenceDbModel
            {
                ResourceId = preference.ResourceId,
                PropertyPool = preference.PropertyPool,
                PropertyId = preference.PropertyId,
                Priorities = serialized
            });
            return ToDomain(inserted.Data!);
        }

        existing.Priorities = serialized;
        await orm.Update(existing);
        return ToDomain(existing);
    }

    public async Task Delete(int resourceId, PropertyPool pool, int propertyId)
    {
        await orm.RemoveAll(x =>
            x.ResourceId == resourceId && x.PropertyPool == pool && x.PropertyId == propertyId);
    }

    public async Task RemoveByResourceIds(IEnumerable<int> resourceIds)
    {
        var ids = resourceIds as HashSet<int> ?? resourceIds.ToHashSet();
        if (ids.Count == 0) return;
        await orm.RemoveAll(x => ids.Contains(x.ResourceId));
    }

    private static string? SerializePriorities(PropertyValueScopePriority[]? priorities) =>
        priorities is not {Length: > 0}
            ? null
            : string.Join(',', priorities.Select(p => $"{(int) p.Scope}:{(p.FallbackOnEmpty ? 1 : 0)}"));

    private static PropertyValueScopePriority[]? DeserializePriorities(string? serialized)
    {
        if (string.IsNullOrEmpty(serialized)) return null;
        return serialized.Split(',', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(entry =>
            {
                var parts = entry.Split(':');
                return new PropertyValueScopePriority
                {
                    Scope = (PropertyValueScope) int.Parse(parts[0]),
                    FallbackOnEmpty = parts.Length > 1 && parts[1] == "1"
                };
            })
            .ToArray();
    }

    private static PropertyValueScopePreference ToDomain(PropertyValueScopePreferenceDbModel row) => new()
    {
        ResourceId = row.ResourceId,
        PropertyPool = row.PropertyPool,
        PropertyId = row.PropertyId,
        Priorities = DeserializePriorities(row.Priorities)
    };
}
