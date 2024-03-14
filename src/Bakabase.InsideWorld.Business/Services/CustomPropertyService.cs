using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services
{
	public class CustomPropertyService : FullMemoryCacheResourceService<InsideWorldDbContext, CustomProperty, int>
	{
		protected CategoryCustomPropertyMappingService CategoryCustomPropertyMappingService =>
			GetRequiredService<CategoryCustomPropertyMappingService>();

		public CustomPropertyService(IServiceProvider serviceProvider) : base(serviceProvider)
		{
		}

		public async Task<List<CustomPropertyDto>> GetDtoList(Expression<Func<CustomProperty, bool>> selector = null,
			bool returnCopy = true)
		{
			var data = await GetAll(selector, returnCopy);

			return data.Select(x => x.ToDto()!).ToList();
		}

		public async Task<Dictionary<int, List<CustomPropertyDto>>> GetByCategoryIds(int[] ids)
		{
			var mappings = await CategoryCustomPropertyMappingService.GetAll(x => ids.Contains(x.CategoryId));
			var propertyIds = mappings.Select(x => x.PropertyId).ToHashSet();
			var properties = await GetDtoList(x => propertyIds.Contains(x.Id));
			var propertyMap = properties.ToDictionary(x => x.Id);

			return mappings.GroupBy(x => x.CategoryId).ToDictionary(x => x.Key,
				x => x.Select(y => propertyMap.GetValueOrDefault(y.PropertyId)).Where(y => y != null).ToList())!;
		}
	}
}