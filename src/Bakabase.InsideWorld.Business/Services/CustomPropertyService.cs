﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Extensions;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services
{
	public class CustomPropertyService : FullMemoryCacheResourceService<InsideWorldDbContext, CustomProperty, int>
	{
		protected CategoryCustomPropertyMappingService CategoryCustomPropertyMappingService =>
			GetRequiredService<CategoryCustomPropertyMappingService>();
		protected CustomPropertyValueService CustomPropertyValueService =>
			GetRequiredService<CustomPropertyValueService>();

		protected ResourceCategoryService ResourceCategoryService => GetRequiredService<ResourceCategoryService>();

		public CustomPropertyService(IServiceProvider serviceProvider) : base(serviceProvider)
		{
		}

		public async Task<List<Abstractions.Models.Domain.CustomProperty>> GetDtoList(Expression<Func<CustomProperty, bool>>? selector = null,
			CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
			bool returnCopy = true)
		{
			var data = await GetAll(selector, returnCopy);
			var dtoList = await ToDtoList(data, additionalItems);
			return dtoList;
		}

		public async Task<Dictionary<int, List<Abstractions.Models.Domain.CustomProperty>>> GetByCategoryIds(int[] ids)
		{
			var mappings = await CategoryCustomPropertyMappingService.GetAll(x => ids.Contains(x.CategoryId));
			var propertyIds = mappings.Select(x => x.PropertyId).ToHashSet();
			var properties = await GetDtoList(x => propertyIds.Contains(x.Id));
			var propertyMap = properties.ToDictionary(x => x.Id);

			return mappings.GroupBy(x => x.CategoryId).ToDictionary(x => x.Key,
				x => x.Select(y => propertyMap.GetValueOrDefault(y.PropertyId)).Where(y => y != null).ToList())!;
		}

		private async Task<List<Abstractions.Models.Domain.CustomProperty>> ToDtoList(List<CustomProperty> properties,
			CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
		{
			var dtoList = properties.Select(p => p.ToDomainModel()!).ToList();
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

		public async Task<Abstractions.Models.Domain.CustomProperty> Add(CustomPropertyAddOrPutDto model)
		{
			var data = await Add(new CustomProperty
			{
				CreatedAt = DateTime.Now,
				Name = model.Name,
				Options = model.Options,
				Type = model.Type
			});

			return data.Data.ToDomainModel()!;
		}

		public async Task<Abstractions.Models.Domain.CustomProperty> Put(int id, CustomPropertyAddOrPutDto model)
		{
			var rsp = await UpdateByKey(id, cp =>
			{
				cp.Name = model.Name;
				cp.Options = model.Options;
				cp.Type = model.Type;
			});

			return rsp.Data.ToDomainModel()!;
		}

		public override async Task<BaseResponse> RemoveByKey(int id)
		{
			await CategoryCustomPropertyMappingService.RemoveAll(x => x.PropertyId == id);
			await CustomPropertyValueService.RemoveAll(x => x.PropertyId == id);
			return await base.RemoveByKey(id);
		}
	}
}