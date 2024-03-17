using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services
{
	public class CategoryCustomPropertyMappingService : FullMemoryCacheResourceService<
		InsideWorldDbContext, CategoryCustomPropertyMapping, int>
	{
		public CategoryCustomPropertyMappingService(IServiceProvider serviceProvider) : base(serviceProvider)
		{
		}

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
	}
}