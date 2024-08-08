using System.Linq.Expressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
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

namespace Bakabase.Modules.Enhancer.Services
{
    public abstract class AbstractCategoryEnhancerOptionsService<TDbContext>(
        ResourceService<TDbContext, Bakabase.Abstractions.Models.Db.CategoryEnhancerOptions, int> orm)
        : ICategoryEnhancerOptionsService where TDbContext : DbContext
    {
        public async Task<List<CategoryEnhancerFullOptions>> GetAll(
            Expression<Func<Bakabase.Abstractions.Models.Db.CategoryEnhancerOptions, bool>>? exp)
        {
            var data = await orm.GetAll(exp);
            return data.Select(d => d.ToDomainModel()!).ToList();
        }

        public async Task<CategoryEnhancerFullOptions?> GetByCategoryAndEnhancer(int cId, int eId)
        {
            var data = await orm.GetFirst(x => x.CategoryId == cId && x.EnhancerId == eId);
            return data?.ToDomainModel();
        }

        public async Task<BaseResponse> PutAll(CategoryEnhancerFullOptions[] options)
        {
            return await orm.UpdateRange(options.Select(o => o.ToDbModel()));
        }

        public async Task<List<CategoryEnhancerFullOptions>> GetByCategory(int categoryId) =>
            await GetAll(x => x.CategoryId == categoryId);

        public async Task<BaseResponse> Patch(int categoryId, int enhancerId,
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
                return await orm.Update(data);
            }

            return await orm.Add(data);
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

        public async Task<BaseResponse> PatchTarget(int categoryId, int enhancerId, int target, string? dynamicTarget,
            CategoryEnhancerTargetOptionsPatchInputModel patches)
        {
            var data = await GetByCategoryAndEnhancer(categoryId, enhancerId);
            if (data == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            var targetOptions =
                data.Options?.TargetOptions?.FirstOrDefault(x =>
                    x.Target == target && x.DynamicTarget == dynamicTarget);
            if (targetOptions == null)
            {
                targetOptions = new EnhancerTargetFullOptions
                {
                    Target = target,
                    DynamicTarget = dynamicTarget
                };
                data.Options ??= new EnhancerFullOptions();
                data.Options.TargetOptions ??= [];
                data.Options.TargetOptions.Add(targetOptions);
            }

            targetOptions.PropertyId = patches.PropertyId ?? targetOptions.PropertyId;
            targetOptions.IntegrateWithAlias = patches.IntegrateWithAlias ?? targetOptions.IntegrateWithAlias;
            targetOptions.AutoMatchMultilevelString =
                patches.AutoMatchMultilevelString ?? targetOptions.AutoMatchMultilevelString;
            targetOptions.AutoGenerateProperties =
                patches.AutoGenerateProperties ?? targetOptions.AutoGenerateProperties;
            targetOptions.DynamicTarget = patches.DynamicTarget ?? targetOptions.DynamicTarget;

            await orm.Update(data.ToDbModel());

            return BaseResponseBuilder.Ok;
        }
    }
}