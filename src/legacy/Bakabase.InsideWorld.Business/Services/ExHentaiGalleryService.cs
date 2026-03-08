using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services;

public class ExHentaiGalleryService(FullMemoryCacheResourceService<BakabaseDbContext, ExHentaiGalleryDbModel, int> orm)
    : IExHentaiGalleryService
{
    public async Task<List<ExHentaiGalleryDbModel>> GetAll()
    {
        return await orm.GetAll();
    }

    public async Task<ExHentaiGalleryDbModel?> GetByGalleryId(long galleryId, string galleryToken)
    {
        return await orm.GetFirstOrDefault(x => x.GalleryId == galleryId && x.GalleryToken == galleryToken);
    }

    public async Task AddOrUpdate(ExHentaiGalleryDbModel gallery)
    {
        var existing = await orm.GetFirstOrDefault(x =>
            x.GalleryId == gallery.GalleryId && x.GalleryToken == gallery.GalleryToken);
        if (existing != null)
        {
            gallery.Id = existing.Id;
            gallery.UpdatedAt = DateTime.Now;
            await orm.Update(gallery);
        }
        else
        {
            gallery.CreatedAt = DateTime.Now;
            gallery.UpdatedAt = DateTime.Now;
            await orm.Add(gallery);
        }
    }

    public async Task AddOrUpdateRange(IEnumerable<ExHentaiGalleryDbModel> galleries)
    {
        foreach (var gallery in galleries)
        {
            await AddOrUpdate(gallery);
        }
    }

    public async Task DeleteById(int id)
    {
        await orm.RemoveByKey(id);
    }
}
