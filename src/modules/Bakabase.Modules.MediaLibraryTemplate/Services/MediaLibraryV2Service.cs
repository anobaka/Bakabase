using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Extensions;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Input;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Services;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.MediaLibraryTemplate.Services;

public class MediaLibraryV2Service<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, MediaLibraryV2DbModel, int> orm)
    : IMediaLibraryV2Service where TDbContext : DbContext
{
    public async Task Add(MediaLibraryV2AddOrPutInputModel model)
    {
        await orm.Add(new MediaLibraryV2DbModel { Path = model.Path, Name = model.Name });
    }

    public async Task Put(int id, MediaLibraryV2AddOrPutInputModel model)
    {
        await orm.UpdateByKey(id, data =>
        {
            data.Name = model.Name;
            data.Path = model.Path;
        });
    }

    public async Task SaveAll(MediaLibraryV2[] models)
    {
        var newData = models.Where(x => x.Id == 0).ToArray();
        var dbData = models.Except(newData).ToArray();
        var ids = dbData.Select(x => x.Id).ToArray();
        await orm.RemoveAll(x => !ids.Contains(x.Id));
        await orm.AddRange(newData.Select(d => d.ToDbModel()).ToList());
        await orm.UpdateRange(dbData.Select(d => d.ToDbModel()).ToList());
    }

    public async Task<MediaLibraryV2> Get(int id)
    {
        return (await orm.GetByKey(id)).ToDomainModel();
    }

    public async Task<List<MediaLibraryV2>> GetAll()
    {
        return (await orm.GetAll()).Select(x => x.ToDomainModel()).ToList();
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }
}