using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Acquisition.Models.Input;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Acquisition.Services;

public class AcquisitionLeadService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, AcquisitionLeadDbModel, int> orm) : IAcquisitionLeadService
    where TDbContext : DbContext
{
    public async Task<List<AcquisitionLead>> GetByResourceId(int resourceId) =>
        (await orm.GetAll(x => x.ResourceId == resourceId))
        .OrderBy(x => x.CreatedAt)
        .Select(x => x.ToDomainModel())
        .ToList();

    public async Task<Dictionary<int, List<AcquisitionLead>>> GetByResourceIds(
        IReadOnlyCollection<int> resourceIds)
    {
        if (resourceIds.Count == 0)
        {
            return [];
        }

        var ids = resourceIds.ToHashSet();
        return (await orm.GetAll(x => ids.Contains(x.ResourceId)))
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ToDomainModel())
            .GroupBy(x => x.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<AcquisitionLead?> FindByValue(AcquisitionLeadKind kind, string value)
    {
        var normalized = value.NormalizeLeadValue();
        var hit = await orm.GetFirstOrDefault(x => x.Kind == kind && x.Value == normalized);
        return hit?.ToDomainModel();
    }

    public async Task<AcquisitionLeadAddResult> Add(int resourceId, AcquisitionLeadAddInputModel model)
    {
        if (!AcquisitionLeadExtensions.StorableKinds.Contains(model.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(model), model.Kind,
                $"'{model.Kind}' leads are not stored. A platform holding is derived from the resource's source links, and a manual choice is not a place to get files from.");
        }

        var normalized = model.Value.NormalizeLeadValue();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("A lead needs a value.", nameof(model));
        }

        // A shared link describes exactly one resource. If it is already attached, either this is a
        // no-op or the user is about to describe two resources with one link.
        var existing = await orm.GetFirstOrDefault(x => x.Kind == model.Kind && x.Value == normalized);
        if (existing != null)
        {
            return existing.ResourceId == resourceId
                ? new AcquisitionLeadAddResult(existing.ToDomainModel(), null)
                : new AcquisitionLeadAddResult(null, existing.ResourceId);
        }

        var added = (await orm.Add(new AcquisitionLeadDbModel
        {
            ResourceId = resourceId,
            Kind = model.Kind,
            Value = normalized,
            Origin = model.Origin,
            Note = model.Note,
            CreatedAt = DateTime.Now
        })).Data!;

        return new AcquisitionLeadAddResult(added.ToDomainModel(), null);
    }

    public async Task Delete(int id) => await orm.RemoveByKey(id);

    public async Task DeleteByResourceIds(IEnumerable<int> resourceIds)
    {
        var ids = resourceIds.ToHashSet();
        if (ids.Count == 0)
        {
            return;
        }

        await orm.RemoveAll(x => ids.Contains(x.ResourceId));
    }

    public async Task MarkUsed(int id, AcquisitionLeadResult result)
    {
        await orm.UpdateByKey(id, x =>
        {
            x.LastUsedAt = DateTime.Now;
            x.LastResult = result;
        });
    }
}
