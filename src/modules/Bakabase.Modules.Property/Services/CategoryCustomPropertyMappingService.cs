using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.Property.Abstractions.Services;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Property.Services
{
    [Obsolete]
    public class CategoryCustomPropertyMappingService<TDbContext>(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<
                TDbContext, CategoryCustomPropertyMapping, int>(serviceProvider),
            ICategoryCustomPropertyMappingService where TDbContext : DbContext
    {
        public async Task BindCustomPropertiesToCategory(int categoryId, int[]? customPropertyIds)
        {
            customPropertyIds ??= [];

            var existingMappings = (await GetAll(x => x.CategoryId == categoryId))!;

            var newMappings = customPropertyIds
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

        public async Task Unlink(int categoryId, int customPropertyId)
        {
            var mapping = await GetFirstOrDefault(x => x.CategoryId == categoryId && x.PropertyId == customPropertyId);
            if (mapping != null)
            {
                await RemoveByKey(mapping.Id);
            }
        }

        public async Task<ListResponse<CategoryCustomPropertyMapping>> AddAll(
            List<CategoryCustomPropertyMapping> resources)
        {
            return await base.AddRange(resources);
        }

        public async Task Sort(int categoryId, int[] propertyIds)
        {
            var mappings = (await GetAll(x => x.CategoryId == categoryId)) ?? [];
            var orderMap = new Dictionary<int, int>();
            for (var i = 0; i < propertyIds.Length; i++)
            {
                orderMap[propertyIds[i]] = i;
            }

            foreach (var mapping in mappings)
            {
                mapping.Order = orderMap.GetValueOrDefault(mapping.PropertyId, int.MaxValue);
            }

            await UpdateRange(mappings);
        }
    }
}