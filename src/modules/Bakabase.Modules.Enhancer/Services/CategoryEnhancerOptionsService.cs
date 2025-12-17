using System.Linq.Expressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Extensions;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using CategoryEnhancerOptions = Bakabase.Abstractions.Models.Db.CategoryEnhancerOptions;

namespace Bakabase.Modules.Enhancer.Services
{
    [Obsolete]
    public class AbstractCategoryEnhancerOptionsService<TDbContext>(
        ResourceService<TDbContext, CategoryEnhancerOptions, int> orm,
        IEnhancerDescriptors enhancerDescriptors)
        : ICategoryEnhancerOptionsService where TDbContext : DbContext
    {
        public async Task<List<CategoryEnhancerFullOptions>> GetAll(
            Expression<Func<Bakabase.Abstractions.Models.Db.CategoryEnhancerOptions, bool>>? exp)
        {
            var data = await orm.GetAll(exp);
            return data.Select(d => d.ToDomainModel(enhancerDescriptors.TryGet(d.EnhancerId))!).ToList();
        }

        public async Task<CategoryEnhancerFullOptions?> GetByCategoryAndEnhancer(int cId, int eId)
        {
            var data = await orm.GetFirst(x => x.CategoryId == cId && x.EnhancerId == eId) ??
                       new CategoryEnhancerOptions {CategoryId = cId, EnhancerId = eId};
            return data?.ToDomainModel(enhancerDescriptors.TryGet(eId));
        }

        public async Task<BaseResponse> PutAll(CategoryEnhancerFullOptions[] options)
        {
            return await orm.UpdateRange(options.Select(o => o.ToDbModel()));
        }

        public async Task<List<CategoryEnhancerFullOptions>> GetByCategory(int categoryId)
        {
            var list = await GetAll(x => x.CategoryId == categoryId);
            var descriptors = enhancerDescriptors.Descriptors.ToList();
            foreach (var enhancer in descriptors)
            {
                var options = list.FirstOrDefault(x => x.EnhancerId == enhancer.Id);
                if (options == null)
                {
                    options = new CategoryEnhancerFullOptions {CategoryId = categoryId, EnhancerId = enhancer.Id};
                    list.Add(options);
                }

                options.AddDefaultOptions(enhancer);
            }

            return list;
        }

        public async Task<SingletonResponse<CategoryEnhancerFullOptions>> Patch(int categoryId, int enhancerId,
            CategoryEnhancerOptionsPatchInputModel model)
        {
            var data = await orm.GetFirst(x => x.CategoryId == categoryId && x.EnhancerId == enhancerId);
            var dataExists = data != null;
            data ??= new Bakabase.Abstractions.Models.Db.CategoryEnhancerOptions
            {
                CategoryId = categoryId,
                EnhancerId = enhancerId
            };

            if (model.Active.HasValue)
            {
                data.Active = model.Active.Value;
            }

            if (model.Options != null)
            {
                data.Options = JsonConvert.SerializeObject(model.Options);
            }

            if (dataExists)
            {
                await orm.Update(data);
            }
            else
            {
                data = (await orm.Add(data)).Data;
            }

            var enhancer = enhancerDescriptors.TryGet(enhancerId);

            return new SingletonResponse<CategoryEnhancerFullOptions>(data!.ToDomainModel(enhancer));
        }

        public async Task<BaseResponse> UnbindTargetProperty(int categoryId, int enhancerId, int target,
            string? dynamicTarget)
        {
            var targetDescriptor = enhancerDescriptors[enhancerId].Targets.FirstOrDefault(t => t.Id == target);
            if (targetDescriptor == null)
            {
                return BaseResponseBuilder.BuildBadRequest(
                    $"Target descriptor for target:{target} of enhancer:{enhancerId} is not found.");
            }

            var data = await GetByCategoryAndEnhancer(categoryId, enhancerId) ?? (await Patch(categoryId, enhancerId,
                    new CategoryEnhancerOptionsPatchInputModel(null, true)))
                .Data;

            await orm.Update(data.ToDbModel());

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> DeleteTarget(int categoryId, int enhancerId, int target, string? dynamicTarget)
        {
            var data = await GetByCategoryAndEnhancer(categoryId, enhancerId);
            if (data == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            var targetOptions =
                data.Options?.TargetOptions?.FirstOrDefault(x =>
                    x.Target == target && x.DynamicTarget == dynamicTarget);
            if (targetOptions != null)
            {
                data.Options!.TargetOptions!.Remove(targetOptions);
                await orm.Update(data.ToDbModel());
            }

            return BaseResponseBuilder.Ok;
        }
    }
}