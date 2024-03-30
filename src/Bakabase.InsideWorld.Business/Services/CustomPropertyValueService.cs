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

namespace Bakabase.InsideWorld.Business.Services
{
	public class
		CustomPropertyValueService : FullMemoryCacheResourceService<InsideWorldDbContext, CustomPropertyValue, int>
	{
		protected CustomPropertyService CustomPropertyService => GetRequiredService<CustomPropertyService>();

		public CustomPropertyValueService(IServiceProvider serviceProvider) : base(serviceProvider)
		{
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