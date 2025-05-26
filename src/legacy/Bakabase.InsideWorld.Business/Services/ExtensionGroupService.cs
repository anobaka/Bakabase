using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services;

public class ExtensionGroupService(FullMemoryCacheResourceService<InsideWorldDbContext, ExtensionGroupDbModel, int> orm)
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
        var domainModels = groups.Select(g => new ExtensionGroup(0, g.Name, g.Extensions)).ToArray();
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
            Extensions = group.Extensions
        };
        await orm.Update(data.ToDbModel());
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }
}