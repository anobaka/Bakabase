using System.Linq.Expressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Extensions;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Enhancer.Services
{
    public abstract class AbstractCategoryEnhancerOptionsService<TDbContext>(IServiceProvider serviceProvider)
        : ResourceService<TDbContext, CategoryEnhancerOptions, int>(serviceProvider),
            ICategoryEnhancerOptionsService where TDbContext : DbContext
    {
        public async Task<List<Bakabase.Abstractions.Models.Domain.CategoryEnhancerOptions>> GetAll(Expression<Func<CategoryEnhancerOptions, bool>>? exp)
        {
            var data = await base.GetAll(exp, false);
            return data.Select(d => d.ToDomainModel()!).ToList();
        }
    }
}