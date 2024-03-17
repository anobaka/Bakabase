using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using CsQuery.Implementation;
using CsQuery.Utility;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using IComponent = Bakabase.InsideWorld.Business.Components.Resource.Components.IComponent;

namespace Bakabase.InsideWorld.Business.Services
{
	public class ResourceCategoryService : Bootstrap.Components.Orm.Infrastructures.ResourceService<InsideWorldDbContext
		,
		ResourceCategory, int>
	{
		protected MediaLibraryService MediaLibraryService => GetRequiredService<MediaLibraryService>();
		protected CategoryCustomPropertyMappingService CategoryCustomPropertyMappingService => GetRequiredService<CategoryCustomPropertyMappingService>();
		protected ResourceService ResourceService => GetRequiredService<ResourceService>();
		protected ComponentService ComponentService => GetRequiredService<ComponentService>();
		protected CategoryComponentService CategoryComponentService => GetRequiredService<CategoryComponentService>();
		protected IStringLocalizer<SharedResource> Localizer => GetRequiredService<IStringLocalizer<SharedResource>>();
		protected CustomPropertyService CustomPropertyService => GetRequiredService<CustomPropertyService>();

		public ResourceCategoryService(IServiceProvider serviceProvider) : base(serviceProvider)
		{
		}

		#region Infrastructures

		public async Task<ListResponse<TComponent>> GetComponents<TComponent>([NotNull] ResourceCategoryDto category,
			ComponentType type)
			where TComponent : class, IComponent
		{
			var componentsData = category?.ComponentsData?.Where(a => a.ComponentType == type).ToArray();
			if (componentsData?.Any() != true)
			{
				return ListResponseBuilder<TComponent>.BuildBadRequest(
					Localizer[SharedResource.Category_ComponentWithTypeHasNotBeenConfigured, type, category.Name]);
			}

			var components =
				await ComponentService.CreateInstances<TComponent>(componentsData.Select(a => a.ComponentKey)
					.ToArray());
			return new ListResponse<TComponent>(components.Keys);
		}

		public async Task<ListResponse<TComponent>> GetComponents<TComponent>(int id, ComponentType type)
			where TComponent : class, IComponent =>
			await GetComponents<TComponent>(await GetByKey(id, ResourceCategoryAdditionalItem.Components), type);

		public async Task<SingletonResponse<TComponent>> GetFirstComponent<TComponent>(int id, ComponentType type)
			where TComponent : class, IComponent
		{
			var rsp = await GetComponents<TComponent>(await GetByKey(id, ResourceCategoryAdditionalItem.Components),
				type);
			return rsp.Code == 0
				? new SingletonResponse<TComponent>(rsp.Data.FirstOrDefault())
				: SingletonResponseBuilder<TComponent>.Build((ResponseCode) rsp.Code, rsp.Message);
		}

		#endregion

		public async Task<BaseResponse> Play(int id, string file)
		{
			var playerRsp = await GetFirstComponent<IPlayer>(id, ComponentType.Player);
			if (playerRsp.Data == null)
			{
				return playerRsp;
			}

			await playerRsp.Data.Play(file);
			return BaseResponseBuilder.Ok;
		}

		public async Task<List<ResourceCategoryDto>> GetAllDto(Expression<Func<ResourceCategory, bool>> selector = null,
			ResourceCategoryAdditionalItem additionalItems = ResourceCategoryAdditionalItem.None)
		{
			var data = await base.GetAll(selector);
			var dtoList = data.Select(d => d.ToDto()).ToArray();
			await Populate(dtoList, additionalItems);
			return dtoList.OrderBy(a => a.Order).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
		}

		public async Task<ResourceCategoryDto> GetByKey(int id,
			ResourceCategoryAdditionalItem additionalItems = ResourceCategoryAdditionalItem.None)
		{
			var c = await base.GetByKey(id);
			var dto = c.ToDto();
			await Populate(dto, additionalItems);
			return dto;
		}

		private async Task Populate(ResourceCategoryDto dto, ResourceCategoryAdditionalItem additionalItems) =>
			await Populate(new[] {dto}, additionalItems);

		private async Task Populate(ResourceCategoryDto[] dtoList, ResourceCategoryAdditionalItem additionalItems)
		{
			foreach (var rca in SpecificEnumUtils<ResourceCategoryAdditionalItem>.Values)
			{
				if (additionalItems.HasFlag(rca))
				{
					switch (rca)
					{
						case ResourceCategoryAdditionalItem.None:
							break;
						case ResourceCategoryAdditionalItem.Components:
						{
							var categoryIds = dtoList.Select(a => a.Id).ToArray();
							var cds = await CategoryComponentService.GetAll(a => categoryIds.Contains(a.CategoryId));
							var cdsMap = cds.GroupBy(a => a.CategoryId).ToDictionary(a => a.Key, a => a.ToArray());

							var componentKeys = cds.Select(a => a.ComponentKey).Distinct().ToArray();
							var descriptors = await ComponentService.GetDescriptors(componentKeys);

							foreach (var a in cdsMap.Values.SelectMany(arr => arr))
							{
								a.Descriptor = descriptors.FirstOrDefault(b => b.Id == a.ComponentKey);
							}

							foreach (var d in dtoList)
							{
								d.ComponentsData = cdsMap.TryGetValue(d.Id, out var t)
									? t
									: Array.Empty<CategoryComponent>();
							}

							break;
						}
						case ResourceCategoryAdditionalItem.Validation:
						{
							foreach (var d in dtoList)
							{
								d.IsValid = true;
								if (d.ComponentsData?.Any() == true)
								{
									var v = await ValidateComponentsData(d.ComponentsData?.Select(a => a.ComponentKey)
										.ToArray());
									if (v.Code != 0)
									{
										d.IsValid = false;
										d.Message = v.Message;
										break;
									}
								}
							}

							break;
						}
						case ResourceCategoryAdditionalItem.CustomProperties:
						{
							var cIds = dtoList.Select(a => a.Id).ToArray();
							var customPropertiesMap = await CustomPropertyService.GetByCategoryIds(cIds);
							foreach (var d in dtoList)
							{
								d.CustomProperties = customPropertiesMap.GetValueOrDefault(d.Id);
							}

							break;
						}
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
			}
		}

		public async Task<SingletonResponse<ResourceCategory>> Add(ResourceCategoryAddRequestModel model)
		{
			var componentKeys = model.ComponentsData?.Select(a => a.ComponentKey).ToArray();
			if (componentKeys?.Any() == true)
			{
				var validation = await ValidateComponentsData(componentKeys);
				if (validation.Code != 0)
				{
					return SingletonResponseBuilder<ResourceCategory>.Build((ResponseCode) validation.Code,
						validation.Message);
				}
			}

			model.EnhancementOptions?.Standardize(model.ComponentsData
				?.Where(a => a.ComponentType == ComponentType.Enhancer).Select(a => a.ComponentKey).ToArray());

			var category = new ResourceCategory
			{
				Color = model.Color,
				CreateDt = DateTime.Now,
				Name = model.Name,
				CoverSelectionOrder = model.CoverSelectionOrder ?? CoverSelectOrder.FilenameAscending,
				Order = model.Order ?? 0,
				GenerateNfo = model.GenerateNfo ?? false,
				EnhancementOptionsJson = model.EnhancementOptions == null
					? null
					: JsonConvert.SerializeObject(model.EnhancementOptions)
			};

			if (componentKeys?.Any() == true)
			{
				var externalTran = DbContext.Database.CurrentTransaction;
				IDbContextTransaction tran = null;
				if (externalTran == null)
				{
					tran = await DbContext.Database.BeginTransactionAsync();
				}

				await base.Add(category);
				await CategoryComponentService.Configure(category.Id, componentKeys);

				if (externalTran == null)
				{
					await tran.CommitAsync();
				}
			}
			else
			{
				await base.Add(category);
			}

			return new SingletonResponse<ResourceCategory>(category);
		}

		public async Task<SingletonResponse<ResourceCategory>> Duplicate(int id,
			ResourceCategoryDuplicateRequestModel model)
		{
			var category = await base.GetByKey(id);

			await using var tran = await DbContext.Database.BeginTransactionAsync();

			var newCategory = (await Add(category.Duplicate(model.Name))).Data;
			await MediaLibraryService.Duplicate(id, newCategory.Id);
			await CategoryComponentService.Duplicate(id, newCategory.Id);

			await tran.CommitAsync();

			return new SingletonResponse<ResourceCategory>(newCategory);
		}

		private async Task<BaseResponse> ValidateComponentsData(string[] keys)
		{
			if (keys != null)
			{
				var cds = await ComponentService.GetDescriptors(keys);
				var invalidResponse = cds.Select(d => d.BuildValidationResponse()).FirstOrDefault(a => a.Code != 0);
				if (invalidResponse != null)
				{
					return invalidResponse;
				}
			}

			return BaseResponseBuilder.Ok;
		}

		public async Task<BaseResponse> ConfigureComponents(int id,
			ResourceCategoryComponentConfigureRequestModel model)
		{
			var componentKeys = model.ComponentKeys ?? Array.Empty<string>();

			var validation = await ValidateComponentsData(model.ComponentKeys);
			if (validation.Code != 0)
			{
				return validation;
			}

			var category = await base.GetByKey(id);
			switch (model.Type)
			{
				case ComponentType.Enhancer:
				{
					ResourceCategoryEnhancementOptions eo;
					if (componentKeys.Any())
					{
						eo = model.EnhancementOptions ?? new ResourceCategoryEnhancementOptions();
						eo.Standardize(componentKeys);
					}
					else
					{
						eo = null;
					}

					category.EnhancementOptionsJson = eo != null ? JsonConvert.SerializeObject(eo) : null;
					break;
				}
				case ComponentType.PlayableFileSelector:
					break;
				case ComponentType.Player:
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			await using var tran = await DbContext.Database.BeginTransactionAsync();
			await CategoryComponentService.Configure(id, componentKeys, model.Type);
			await DbContext.SaveChangesAsync();
			await tran.CommitAsync();
			return BaseResponseBuilder.Ok;
		}

		public async Task<BaseResponse> Patch(int id, ResourceCategoryUpdateRequestModel model)
		{
			var category = await base.GetByKey(id);
			if (model.Name.IsNotEmpty())
			{
				category.Name = model.Name;
			}

			if (model.Order.HasValue)
			{
				category.Order = model.Order.Value;
			}

			if (model.Color.IsNotEmpty())
			{
				category.Color = model.Color;
			}

			if (model.CoverSelectionOrder.HasValue)
			{
				category.CoverSelectionOrder = model.CoverSelectionOrder.Value;
			}

			if (model.GenerateNfo.HasValue)
			{
				category.GenerateNfo = model.GenerateNfo.Value;
			}

			await DbContext.SaveChangesAsync();
			return BaseResponseBuilder.Ok;
		}

		/// <summary>
		/// All related data will be deleted too.
		/// </summary>
		/// <param name="id"></param>
		/// <returns></returns>
		public async Task<BaseResponse> DeleteAndClearAllRelatedData(int id)
		{
			await MediaLibraryService.RemoveAll(x => x.CategoryId == id);
			await ResourceService.LogicallyRemoveByCategoryId(id);
			await CategoryComponentService.RemoveAll(x => x.CategoryId == id);

			return await base.RemoveByKey(id);
		}

		public async Task<BaseResponse> Sort(int[] ids)
		{
			var categories = (await GetByKeys(ids)).ToDictionary(t => t.Id, t => t);
			var changed = new List<ResourceCategory>();
			for (var i = 0; i < ids.Length; i++)
			{
				var id = ids[i];
				if (categories.TryGetValue(id, out var t) && t.Order != i)
				{
					t.Order = i;
					changed.Add(t);
				}
			}

			return await UpdateRange(changed);
		}

		public async Task<BaseResponse> SaveDataFromSetupWizard(CategorySetupWizardRequestModel model)
		{
			var categoryModel = new ResourceCategoryAddRequestModel
			{
				Color = model.Category.Color,
				ComponentsData = model.Category.ComponentsData,
				CoverSelectionOrder = model.Category.CoverSelectionOrder,
				EnhancementOptions = model.Category.EnhancementOptions,
				GenerateNfo = model.Category.GenerateNfo,
				Name = model.Category.Name,
				Order = model.Category.Order
			};

			BaseResponse categoryOperationRsp;
			var categoryId = model.Category.Id;

			await using var tran = await DbContext.Database.BeginTransactionAsync();

			if (model.Category.Id > 0)
			{
				ResourceCategoryEnhancementOptions eo = null;
				var componentKeys = model.Category.ComponentsData?.Select(a => a.ComponentKey).ToArray() ??
				                    Array.Empty<string>();
				if (componentKeys.Any())
				{
					var validation = await ValidateComponentsData(componentKeys);
					if (validation.Code != 0)
					{
						return validation;
					}

					var descriptors = await ComponentService.GetDescriptors(componentKeys);
					var enhancerKeys = descriptors.Where(a => a.ComponentType == ComponentType.Enhancer)
						.Select(a => a.Id).ToArray();
					if (enhancerKeys.Any())
					{
						eo = model.Category.EnhancementOptions ?? new();
						eo.Standardize(enhancerKeys);
					}
				}

				await CategoryComponentService.Configure(model.Category.Id, componentKeys);
				var category = await base.GetByKey(model.Category.Id);
				category.EnhancementOptionsJson = eo == null ? null : JsonConvert.SerializeObject(eo);
				await DbContext.SaveChangesAsync();

				categoryOperationRsp = await Patch(model.Category.Id, categoryModel);
			}
			else
			{
				categoryOperationRsp = await Add(categoryModel);
				if (categoryOperationRsp.Code == 0)
				{
					categoryId = ((SingletonResponse<ResourceCategory>) categoryOperationRsp).Data.Id;
				}
			}

			if (categoryOperationRsp.Code != 0)
			{
				return categoryOperationRsp;
			}

			if (model.MediaLibraries?.Length > 0)
			{
				foreach (var m in model.MediaLibraries)
				{
					BaseResponse mediaLibraryOperationRsp;

					if (m.Id > 0)
					{
						mediaLibraryOperationRsp = await MediaLibraryService.Patch(m.Id,
							new MediaLibraryPatchRequestModel
							{
								Name = m.Name,
								Order = m.Order,
								PathConfigurations = m.PathConfigurations
							});
					}
					else
					{
						mediaLibraryOperationRsp = await MediaLibraryService.Add(new MediaLibraryCreateRequestModel
						{
							CategoryId = categoryId,
							Name = m.Name,
							PathConfigurations = m.PathConfigurations
						});
					}

					if (mediaLibraryOperationRsp.Code != 0)
					{
						return mediaLibraryOperationRsp;
					}
				}
			}

			await tran.CommitAsync();

			if (model.SyncAfterSaving)
			{
				MediaLibraryService.SyncInBackgroundTask();
			}

			return BaseResponseBuilder.Ok;
		}

		public async Task<BaseResponse> BindCustomProperties(int id, ResourceCategoryCustomPropertyBindRequestModel model)
		{
			await CategoryCustomPropertyMappingService.BindCustomPropertiesToCategory(id,
				model.CustomPropertyIds);
			return BaseResponseBuilder.Ok;
		}
	}
}