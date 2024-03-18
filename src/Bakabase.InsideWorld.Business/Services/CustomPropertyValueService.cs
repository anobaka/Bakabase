using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using static Bakabase.InsideWorld.Models.Models.Aos.ResourceSearchDto;

namespace Bakabase.InsideWorld.Business.Services
{
	public class
		CustomPropertyValueService : FullMemoryCacheResourceService<InsideWorldDbContext, CustomPropertyValue, int>
	{
		protected CustomPropertyService CustomPropertyService => GetRequiredService<CustomPropertyService>();

		public CustomPropertyValueService(IServiceProvider serviceProvider) : base(serviceProvider)
		{
		}

		public async Task<List<int>> SearchResourceIds(ResourceSearchByCustomPropertyValuesRequestModel model)
		{
			if (model.CustomPropertyValueSearchModels?.Any() == true)
			{
				var allValueDtoList = await GetDtoList(null, CustomPropertyValueAdditionalItem.None, false);
				var resourceValueMap =
					allValueDtoList.GroupBy(x => x.ResourceId).ToDictionary(x => x.Key,
						x => x.GroupBy(y => y.PropertyId).ToDictionary(y => y.Key, y => y.First()));
				var resourceIds = new List<int>();
				foreach (var (rId, valueMap) in resourceValueMap)
				{
					var match = true;
					foreach (var m in model.CustomPropertyValueSearchModels)
					{
						var value = valueMap.GetValueOrDefault(m.PropertyId);
						var hit = (value == null && m.Operation == CustomPropertyValueSearchOperation.IsNull) ||
						          value?.IsMatch(m) == true;
						if (hit)
						{
							if (model.Combination == ResourceSearchByCustomPropertyValuesCombination.Or)
							{
								// avoid unnecessary matches
								break;
							}
						}
						else
						{
							if (model.Combination == ResourceSearchByCustomPropertyValuesCombination.And)
							{
								// avoid unnecessary matches
								match = false;
								break;
							}
						}
					}

					if (match)
					{
						resourceIds.Add(rId);
					}
				}

				return resourceIds;
			}

			return [];
		}

		public async Task<List<CustomPropertyValueDto>> GetDtoList(Expression<Func<CustomPropertyValue, bool>>? exp,
			CustomPropertyValueAdditionalItem additionalItems, bool returnCopy)
		{
			var data = await GetAll(exp, returnCopy);

			return await ToDtoList(data, additionalItems, returnCopy);
		}

		protected async Task<List<CustomPropertyValueDto>> ToDtoList(List<CustomPropertyValue> values,
			CustomPropertyValueAdditionalItem additionalItems, bool returnCopy)
		{
			var propertyIds = values.Select(v => v.PropertyId).ToHashSet();
			var properties =
				await CustomPropertyService.GetDtoList(x => propertyIds.Contains(x.Id),
					CustomPropertyAdditionalItem.None, returnCopy);
			var propertyMap = properties.ToDictionary(x => x.Id);
			var dtoList = values
				.Select(v => CustomPropertyExtensions.Descriptors[propertyMap[v.PropertyId].Type].BuildValueDto(v)!).ToList();

			foreach (var dto in dtoList)
			{
				dto.Property = propertyMap[dto.PropertyId];
			}

			return dtoList;
		}

		public async Task<BaseResponse> SetResourceValue(int resourceId, int propertyId,
			ResourceCustomPropertyValuePutRequestModel model)
		{
			var value = await GetFirst(x => x.ResourceId == resourceId && x.PropertyId == propertyId);
			if (value == null)
			{
				value = new CustomPropertyValue
				{
					ResourceId = resourceId,
					PropertyId = propertyId,
					Value = model.Value
				};
				return await Add(value);
			}
			else
			{
				value.Value = model.Value;
				return await Update(value);
			}
		}
	}
}