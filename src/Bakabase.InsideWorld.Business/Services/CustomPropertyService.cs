using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Services
{
	public class CustomPropertyService : FullMemoryCacheResourceService<InsideWorldDbContext, CustomProperty, int>
	{
		protected CategoryCustomPropertyMappingService CategoryCustomPropertyMappingService =>
			GetRequiredService<CategoryCustomPropertyMappingService>();

		protected ResourceCategoryService ResourceCategoryService => GetRequiredService<ResourceCategoryService>();

		public CustomPropertyService(IServiceProvider serviceProvider) : base(serviceProvider)
		{
		}

		public async Task<List<CustomPropertyDto>> GetDtoList(Expression<Func<CustomProperty, bool>>? selector = null,
			CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
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

		private async Task<List<CustomPropertyDto>> ToDtoList(List<CustomProperty> properties,
			CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
		{
			var dtoList = properties.Select(p => p.ToDto()!).ToList();
			foreach (var ai in SpecificEnumUtils<CustomPropertyAdditionalItem>.Values)
			{
				if (additionalItems.HasFlag(ai))
				{
					switch (ai)
					{
						case CustomPropertyAdditionalItem.None:
							break;
						case CustomPropertyAdditionalItem.Category:
						{
							var propertyIds = properties.Select(x => x.Id).ToHashSet();
							var mappings =
								(await CategoryCustomPropertyMappingService.GetAll(x =>
									propertyIds.Contains(x.PropertyId)))!;
							var categoryIds = mappings.Select(x => x.CategoryId).ToHashSet();
							var categories = await ResourceCategoryService.GetAllDto(x => categoryIds.Contains(x.Id));
							var categoryMap = categories.ToDictionary(x => x.Id);
							var propertyCategoryIdMap = mappings.GroupBy(x => x.PropertyId)
								.ToDictionary(x => x.Key, x => x.Select(y => y.CategoryId).ToHashSet());

							foreach (var dto in dtoList)
							{
								dto.Categories = propertyCategoryIdMap.GetValueOrDefault(dto.Id)
									?.Select(x => categoryMap.GetValueOrDefault(x)).Where(x => x != null).ToList()!;
							}

							break;
						}
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
			}

			return dtoList;
		}

		public async Task<CustomPropertyDto> Add(CustomPropertyAddOrPutRequestModel model)
		{
			var data = await Add(new CustomProperty
			{
				CreatedAt = DateTime.Now,
				Name = model.DisplayName,
				Options = model.Options,
				Type = model.Type
			});

			return data.Data.ToDto()!;
		}

		public async Task<CustomPropertyDto> Update(int id, CustomPropertyAddOrPutRequestModel model)
		{
			var rsp = await UpdateByKey(id, cp =>
			{
				cp.Name = model.DisplayName;
				cp.Options = model.Options;
				cp.Type = model.Type;
			});

			return rsp.Data.ToDto()!;
		}
	}
}