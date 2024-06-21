using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.CustomProperty.Services
{
    public abstract class AbstractCategoryCustomPropertyMappingService<TDbContext>(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<
                TDbContext, CategoryCustomPropertyMapping, int>(serviceProvider),
            ICategoryCustomPropertyMappingService where TDbContext : DbContext
    {
        public async Task BindCustomPropertiesToCategory(int categoryId, int[]? customPropertyIds)
        {
            var existingMappings = (await GetAll(x => x.CategoryId == categoryId))!;

            var newMappings = (customPropertyIds ?? [])
                .Select(x => new CategoryCustomPropertyMapping
                {
                    CategoryId = categoryId,
                    PropertyId = x
                })
                .ToList();

            var toBeRemoved = existingMappings.Where(x => !customPropertyIds.Contains(x.PropertyId)).ToList();
            var toBeAdded = newMappings.Where(x => existingMappings.All(y => y.PropertyId != x.PropertyId)).ToList();

            await RemoveRange(toBeRemoved);
            await AddRange(toBeAdded);
        }

        public async Task<ListResponse<CategoryCustomPropertyMapping>> AddAll(
            List<CategoryCustomPropertyMapping> resources)
        {
            return await base.AddRange(resources);
        }
    }
}