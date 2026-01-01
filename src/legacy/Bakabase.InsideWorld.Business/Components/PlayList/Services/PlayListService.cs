using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PlayList.Extensions;
using Bakabase.InsideWorld.Business.Components.PlayList.Models.Db;
using Bakabase.InsideWorld.Business.Components.PlayList.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.PlayList.Services
{
    public class PlayListService : ResourceService<BakabaseDbContext, PlayListDbModel, int>, IPlayListService
    {
        public PlayListService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<BaseResponse> Patch(int id, PlayListPatchInputModel model)
        {
            var entity = await GetByKey(id);
            if (entity == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            // Apply selective updates
            if (model.Name != null)
            {
                // Check for duplicate names
                if (await DbContext.Set<PlayListDbModel>()
                        .AnyAsync(x => x.Name == model.Name && x.Id != id))
                {
                    return BaseResponseBuilder.BuildBadRequest($"Playlist with name '{model.Name}' already exists");
                }

                entity.Name = model.Name;
            }

            if (model.Items != null)
            {
                entity.ItemsJson = Newtonsoft.Json.JsonConvert.SerializeObject(model.Items);
            }

            if (model.Interval.HasValue)
            {
                entity.Interval = model.Interval.Value;
            }

            if (model.Order.HasValue)
            {
                entity.Order = model.Order.Value;
            }

            return await Update(entity);
        }

        public async Task<BaseResponse> Put(int id, Models.Domain.PlayList model)
        {
            var existingEntity = await GetByKey(id);
            if (existingEntity == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            // Check for duplicate names
            if (await DbContext.Set<PlayListDbModel>()
                    .AnyAsync(x => x.Name == model.Name && x.Id != id))
            {
                return BaseResponseBuilder.BuildBadRequest($"Playlist with name '{model.Name}' already exists");
            }

            // Update the entity with new values
            model.Id = id; // Ensure ID is set
            var dbModel = model.ToDbModel();

            return await Update(dbModel);
        }

        public async Task<BaseResponse> Delete(int id)
        {
            return await RemoveByKey(id);
        }

        public async Task<Models.Domain.PlayList?> Get(int id)
        {
            var entity = await GetByKey(id);
            return entity?.ToDomainModel();
        }

        public async Task<List<Models.Domain.PlayList>> GetAll()
        {
            var entities = await base.GetAll();
            return entities.Select(e => e.ToDomainModel()).OrderBy(d => d.Order).ToList();
        }

        public async Task<BaseResponse> Add(PlayListAddInputModel model)
        {
            var dbModel = model.ToDomainModel().ToDbModel();
            return await base.Add(dbModel);
        }
    }
}