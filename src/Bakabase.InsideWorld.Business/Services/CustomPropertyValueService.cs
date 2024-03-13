using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Orm;
using static Bakabase.InsideWorld.Models.Models.Aos.ResourceSearchDto;

namespace Bakabase.InsideWorld.Business.Services
{
	public class CustomPropertyValueService : FullMemoryCacheResourceService<InsideWorldDbContext, CustomPropertyValue, int>
	{
		public CustomPropertyValueService(IServiceProvider serviceProvider) : base(serviceProvider)
		{
		}

		public async Task<List<CustomPropertyValueDto>> Search(ResourceSearchByCustomPropertyValuesRequestModel model)
		{
			if (model.CustomPropertyValueSearchModels?.Any() == true)
			{
				var propertyIds = model.CustomPropertyValueSearchModels.Select(x => x.PropertyId).ToHashSet();
				var allValues = await GetAll(x => propertyIds.Contains(x.PropertyId));
				var allValueDtoList = allValues.Select(v => v.ToDto()!).ToList();
				var resourceValueMap =
					allValueDtoList.GroupBy(x => x.ResourceId).ToDictionary(x => x.Key,
						x => x.GroupBy(y => y.PropertyId).ToDictionary(y => y.Key, y => y.First()));
				foreach (var (rId, valueMap) in resourceValueMap)
				{
					foreach (var m in model.CustomPropertyValueSearchModels)
					{
						var value = valueMap.GetValueOrDefault(m.PropertyId);
						switch (m.Operation)
						{
							case CustomPropertySearchOperation.Equals:
								break;
							case CustomPropertySearchOperation.NotEquals:
								break;
							case CustomPropertySearchOperation.Contains:
								break;
							case CustomPropertySearchOperation.NotContains:
								break;
							case CustomPropertySearchOperation.Null:
								break;
							case CustomPropertySearchOperation.NotNull:
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}
					}
				}
			}

			return [];
		}
	}
}
