using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services;

public class ExtensionGroupService(FullMemoryCacheResourceService<BakabaseDbContext, ExtensionGroupDbModel, int> orm)
    : IExtensionGroupService
{
    public async Task<ExtensionGroup[]> GetAll() => (await orm.GetAll()).Select(x => x.ToDomainModel()).ToArray();

    public async Task<ExtensionGroup> Get(int id)
    {
        var eg = await orm.GetByKey(id);
        return eg.ToDomainModel();
    }

    public async Task<ExtensionGroup[]> AddRange(ExtensionGroupAddInputModel[] groups)
    {
        var domainModels = groups.Select(g => new ExtensionGroup
        {
            Id = 0,
            Name = g.Name,
            // Normalize extensions to ensure they all start with a single dot
            Extensions = g.Extensions.NormalizeExtensions()
        }).ToArray();
        var dbModels = domainModels.Select(d => d.ToDbModel()).ToList();
        var data = (await orm.AddRange(dbModels)).Data!;
        return data.Select(d => d.ToDomainModel()).ToArray();
    }

    public async Task<ExtensionGroup> Add(ExtensionGroupAddInputModel group)
    {
        return (await AddRange([group]))[0];
    }

    public async Task Put(int id, ExtensionGroupPutInputModel group)
    {
        var data = await Get(id);
        data = data with
        {
            Name = group.Name,
            // Normalize extensions to ensure they all start with a single dot
            Extensions = group.Extensions.NormalizeExtensions()
        };
        await orm.Update(data.ToDbModel());
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }
}