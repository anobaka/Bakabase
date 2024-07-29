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
    public abstract class AbstractCategoryEnhancerOptionsService<TDbContext>(ResourceService<TDbContext, CategoryEnhancerOptions, int> orm)
        : ICategoryEnhancerOptionsService where TDbContext : DbContext
    {
        public async Task<List<CategoryEnhancerFullOptions>> GetAll(
            Expression<Func<CategoryEnhancerOptions, bool>>? exp)
        {
            var data = await orm.GetAll(exp);
            return data.Select(d => d.ToDomainModel()!).ToList();
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
                return await orm.Update(data);
            }

            return await orm.Add(data);
        }
    }
}