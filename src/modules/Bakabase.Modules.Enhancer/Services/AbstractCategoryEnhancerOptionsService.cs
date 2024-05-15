using System.Linq.Expressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Extensions;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Input;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Bakabase.Modules.Enhancer.Services
{
    public abstract class AbstractCategoryEnhancerOptionsService<TDbContext>(IServiceProvider serviceProvider)
        : ResourceService<TDbContext, CategoryEnhancerOptions, int>(serviceProvider),
            ICategoryEnhancerOptionsService where TDbContext : DbContext
    {
        public async Task<List<CategoryEnhancerFullOptions>> GetAll(
            Expression<Func<CategoryEnhancerOptions, bool>>? exp)
        {
            var data = await base.GetAll(exp, false);
            return data.Select(d => d.ToDomainModel()!).ToList();
        }

        public async Task<List<CategoryEnhancerFullOptions>> GetByCategory(int categoryId) =>
            await GetAll(x => x.CategoryId == categoryId);

        public async Task<BaseResponse> Patch(int categoryId, int enhancerId,
            CategoryEnhancerOptionsPatchInputModel model)
        {
            var data = await GetFirst(x => x.CategoryId == categoryId && x.EnhancerId == enhancerId);
            var dataExists = data != null;
            data ??= new CategoryEnhancerOptions
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
                return await Update(data);
            }

            return await Add(data);
        }
    }
}