using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services;

public class ExtensionGroupService(FullMemoryCacheResourceService<InsideWorldDbContext, ExtensionGroupDbModel, int> orm)
    : IExtensionGroupService
{
    public async Task<ExtensionGroup[]> GetAll() => (await orm.GetAll()).Select(x => x.ToDomainModel()).ToArray();

    public async Task Add(ExtensionGroup group)
    {
        await orm.Add(group.ToDbModel());
    }

    public async Task Put(int id, ExtensionGroup group)
    {
        var data = await orm.GetByKey(id);
        var putModel = group.ToDbModel();
        data = data with
        {
            Name = putModel.Name,
            Extensions = putModel.Extensions
        };
        await orm.Update(data);
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }
}