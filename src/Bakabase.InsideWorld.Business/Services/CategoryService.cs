using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Models.View.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Helpers;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Dto;
using Bakabase.InsideWorld.Business.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using CsQuery.ExtensionMethods;
using CsQuery.ExtensionMethods.Internal;
using CsQuery.Implementation;
using CsQuery.Utility;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Namotion.Reflection;
using Newtonsoft.Json;
using static Bakabase.Abstractions.Models.View.CategoryResourceDisplayNameViewModel;
using Category = Bakabase.Abstractions.Models.Domain.Category;
using CategoryEnhancerOptions = Bakabase.Abstractions.Models.Domain.CategoryEnhancerOptions;
using CustomProperty = Bakabase.Abstractions.Models.Domain.CustomProperty;
using IComponent = Bakabase.Abstractions.Components.Component.IComponent;
using Resource = Bakabase.Abstractions.Models.Domain.Resource;

namespace Bakabase.InsideWorld.Business.Services
{
    public class CategoryService(
        IServiceProvider serviceProvider,
        ResourceService<InsideWorldDbContext, Bakabase.Abstractions.Models.Db.Category, int> orm)
        : BootstrapService(serviceProvider), ICategoryService
    {
        protected IMediaLibraryService MediaLibraryService => GetRequiredService<IMediaLibraryService>();

        protected ICategoryCustomPropertyMappingService CategoryCustomPropertyMappingService =>
            GetRequiredService<ICategoryCustomPropertyMappingService>();

        protected IResourceService ResourceService => GetRequiredService<IResourceService>();
        protected ComponentService ComponentService => GetRequiredService<ComponentService>();
        protected CategoryComponentService CategoryComponentService => GetRequiredService<CategoryComponentService>();
        protected IStringLocalizer<SharedResource> Localizer => GetRequiredService<IStringLocalizer<SharedResource>>();
        protected ICustomPropertyService CustomPropertyService => GetRequiredService<ICustomPropertyService>();

        protected ICategoryEnhancerOptionsService CategoryEnhancerOptionsService =>
            GetRequiredService<ICategoryEnhancerOptionsService>();

        protected Dictionary<int, ICustomPropertyDescriptor> CustomPropertyDescriptorMap =>
            GetRequiredService<IEnumerable<ICustomPropertyDescriptor>>().ToDictionary(d => d.Type, d => d);

        protected Dictionary<StandardValueType, IStandardValueHandler> StandardValueHandlerMap =>
            GetRequiredService<IEnumerable<IStandardValueHandler>>().ToDictionary(d => d.Type, d => d);

        protected ISpecialTextService SpecialTextService => GetRequiredService<ISpecialTextService>();

        #region Infrastructures

        public async Task<ListResponse<TComponent>> GetComponents<TComponent>(
            Abstractions.Models.Domain.Category category,
            ComponentType type)
            where TComponent : class, IComponent
        {
            var componentsData = category.ComponentsData?.Where(a => a.ComponentType == type).ToArray();
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
            where TComponent : class, IComponent
        {
            var c = await Get(id, CategoryAdditionalItem.Components);
            if (c == null)
            {
                return ListResponseBuilder<TComponent>.BadRequest;
            }

            return await GetComponents<TComponent>(c, type);
        }

        public async Task<SingletonResponse<TComponent?>> GetFirstComponent<TComponent>(int id, ComponentType type)
            where TComponent : class, IComponent
        {
            var c = await Get(id, CategoryAdditionalItem.Components);
            if (c == null)
            {
                return SingletonResponseBuilder<TComponent?>.BadRequest;
            }

            var rsp = await GetComponents<TComponent>(c, type);
            var firstComponent = rsp.Data?.FirstOrDefault();
            return rsp.Code == 0
                ? new SingletonResponse<TComponent?>(firstComponent)
                : SingletonResponseBuilder<TComponent?>.Build((ResponseCode) rsp.Code, rsp.Message);
        }

        #endregion

        public async Task<List<Abstractions.Models.Domain.Category>> GetAll(
            Expression<Func<Bakabase.Abstractions.Models.Db.Category, bool>>? selector = null,
            CategoryAdditionalItem additionalItems = CategoryAdditionalItem.None)
        {
            var data = await orm.GetAll(selector);
            var dtoList = data.Select(d => d.ToDomainModel()).ToArray();
            await Populate(dtoList, additionalItems);
            return dtoList.OrderBy(a => a.Order).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public async Task<Category?> Get(int id,
            CategoryAdditionalItem additionalItems = CategoryAdditionalItem.None)
        {
            var c = await orm.GetByKey(id);
            var dto = c?.ToDomainModel();
            if (dto != null)
            {
                await Populate(dto, additionalItems);
            }
            return dto;
        }

        private async Task Populate(Abstractions.Models.Domain.Category dto, CategoryAdditionalItem additionalItems) =>
            await Populate(new[] {dto}, additionalItems);

        private async Task Populate(Abstractions.Models.Domain.Category[] dtoList,
            CategoryAdditionalItem additionalItems)
        {
            foreach (var rca in SpecificEnumUtils<CategoryAdditionalItem>.Values)
            {
                if (additionalItems.HasFlag(rca))
                {
                    switch (rca)
                    {
                        case CategoryAdditionalItem.None:
                            break;
                        case CategoryAdditionalItem.Components:
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
                        case CategoryAdditionalItem.Validation:
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
                        case CategoryAdditionalItem.CustomProperties:
                        {
                            var cIds = dtoList.Select(a => a.Id).ToArray();
                            var customPropertiesMap = await CustomPropertyService.GetByCategoryIds(cIds);
                            foreach (var d in dtoList)
                            {
                                d.CustomProperties = customPropertiesMap.GetValueOrDefault(d.Id)
                                    ?.OfType<CustomProperty>().ToList();
                            }

                            break;
                        }
                        case CategoryAdditionalItem.EnhancerOptions:
                        {
                            var cIds = dtoList.Select(a => a.Id).ToArray();
                            var enhancerOptionsMap =
                                (await CategoryEnhancerOptionsService.GetAll(c => cIds.Contains(c.CategoryId)))
                                .GroupBy(d => d.CategoryId).ToDictionary(d => d.Key, d => d.ToList());
                            foreach (var d in dtoList)
                            {
                                d.EnhancerOptions = enhancerOptionsMap.GetValueOrDefault(d.Id)
                                    ?.Cast<CategoryEnhancerOptions>().ToList();
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
        }

        public async Task<SingletonResponse<Category>> Add(CategoryAddInputModel model)
        {
            var componentKeys = model.ComponentsData?.Select(a => a.ComponentKey).ToArray();
            if (componentKeys?.Any() == true)
            {
                var validation = await ValidateComponentsData(componentKeys);
                if (validation.Code != 0)
                {
                    return SingletonResponseBuilder<Category>.Build((ResponseCode) validation.Code,
                        validation.Message);
                }
            }

            model.EnhancementOptions?.Standardize(model.ComponentsData
                ?.Where(a => a.ComponentType == ComponentType.Enhancer).Select(a => a.ComponentKey).ToArray());

            var category = new Category
            {
                Color = model.Color,
                CreateDt = DateTime.Now,
                Name = model.Name,
                CoverSelectionOrder = model.CoverSelectionOrder ?? CoverSelectOrder.FilenameAscending,
                Order = model.Order ?? 0,
                GenerateNfo = model.GenerateNfo ?? false,
                EnhancementOptions = model.EnhancementOptions,
            };

            if (componentKeys?.Any() == true)
            {
                var externalTran = orm.DbContext.Database.CurrentTransaction;
                IDbContextTransaction tran = null;
                if (externalTran == null)
                {
                    tran = await orm.DbContext.Database.BeginTransactionAsync();
                }

                category = (await orm.Add(category.ToDbModel())).Data.ToDomainModel();
                await CategoryComponentService.Configure(category.Id, componentKeys);

                if (externalTran == null)
                {
                    await tran.CommitAsync();
                }
            }
            else
            {
                category = (await orm.Add(category.ToDbModel())).Data.ToDomainModel();
            }

            return new SingletonResponse<Category>(category);
        }

        public async Task<SingletonResponse<Category>> Duplicate(int id, CategoryDuplicateInputModel model)
        {
            var category = await Get(id);
            var copy = category.Duplicate(model.Name);
            await using var tran = await orm.DbContext.Database.BeginTransactionAsync();

            var newCategory = (await orm.Add(copy.ToDbModel())).Data;
            await MediaLibraryService.DuplicateAllInCategory(id, newCategory.Id);
            await CategoryComponentService.DuplicateAllInCategory(id, newCategory.Id);

            await tran.CommitAsync();

            return new SingletonResponse<Category>(newCategory.ToDomainModel());
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

        public async Task<BaseResponse> ConfigureComponents(int id, CategoryComponentConfigureInputModel model)
        {
            var componentKeys = model.ComponentKeys ?? Array.Empty<string>();

            var validation = await ValidateComponentsData(model.ComponentKeys);
            if (validation.Code != 0)
            {
                return validation;
            }

            var category = await orm.GetByKey(id);
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

            await using var tran = await orm.DbContext.Database.BeginTransactionAsync();
            await CategoryComponentService.Configure(id, componentKeys, model.Type);
            await orm.DbContext.SaveChangesAsync();
            await tran.CommitAsync();
            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> Patch(int id, CategoryPatchInputModel model)
        {
            var category = await orm.GetByKey(id);
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

            if (!string.IsNullOrEmpty(model.ResourceDisplayNameTemplate))
            {
                category.ResourceDisplayNameTemplate = model.ResourceDisplayNameTemplate;
            }

            await orm.DbContext.SaveChangesAsync();
            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// All related data will be deleted too.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<BaseResponse> Delete(int id)
        {
            await MediaLibraryService.DeleteAll(x => x.CategoryId == id);
            await CategoryComponentService.RemoveAll(x => x.CategoryId == id);

            return await orm.RemoveByKey(id);
        }

        public async Task<BaseResponse> Sort(int[] ids)
        {
            var categories = (await GetByKeys(ids, CategoryAdditionalItem.None)).ToDictionary(t => t.Id, t => t);
            var changed = new List<Category>();
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (categories.TryGetValue(id, out var t) && t.Order != i)
                {
                    t.Order = i;
                    changed.Add(t);
                }
            }

            return await orm.UpdateRange(changed.Select(c => c.ToDbModel()));
        }

        public async Task<BaseResponse> SaveDataFromSetupWizard(CategorySetupWizardInputModel model)
        {
            var categoryModel = new CategoryAddInputModel
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

            await using var tran = await orm.DbContext.Database.BeginTransactionAsync();

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
                var category = await orm.GetByKey(model.Category.Id);
                category.EnhancementOptionsJson = eo == null ? null : JsonConvert.SerializeObject(eo);
                await orm.DbContext.SaveChangesAsync();

                categoryOperationRsp = await Patch(model.Category.Id, categoryModel);
            }
            else
            {
                categoryOperationRsp = await Add(categoryModel);
                if (categoryOperationRsp.Code == 0)
                {
                    categoryId = ((SingletonResponse<Category>) categoryOperationRsp).Data.Id;
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
                            new MediaLibraryPatchDto()
                            {
                                Name = m.Name,
                                Order = m.Order,
                                PathConfigurations = m.PathConfigurations
                            });
                    }
                    else
                    {
                        mediaLibraryOperationRsp = await MediaLibraryService.Add(new MediaLibraryAddDto
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
                MediaLibraryService.StartSyncing([categoryId], null);
            }

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> BindCustomProperty(int categoryId, int propertyId)
        {
            var category = await Get(categoryId, CategoryAdditionalItem.CustomProperties);
            var pIds = category.CustomProperties?.Select(a => a.Id).ToHashSet() ?? [];
            pIds.Add(propertyId);
            return await BindCustomProperties(categoryId,
                new CategoryCustomPropertyBindInputModel {CustomPropertyIds = pIds.ToArray()});
        }

        public async Task<BaseResponse> BindCustomProperties(int id,
            CategoryCustomPropertyBindInputModel model)
        {
            await CategoryCustomPropertyMappingService.BindCustomPropertiesToCategory(id,
                model.CustomPropertyIds);
            return BaseResponseBuilder.Ok;
        }

        public Segment[] BuildDisplayNameSegmentsForResource(Resource resource, string template,
            (string Left, string Right)[] wrappers)
        {
            var matcherPropertyMap = resource.Properties?.GetValueOrDefault((int) ResourcePropertyType.Custom)?.Values
                .GroupBy(d => d.Name)
                .ToDictionary(d => $"{{{d.Key}}}", d => d.First()) ?? [];

            var replacements = matcherPropertyMap.ToDictionary(d => d.Key,
                d =>
                {
                    var value = d.Value.Values?.FirstOrDefault()?.BizValue;
                    if (value != null)
                    {
                        var stdValueHandler = StandardValueHandlerMap[d.Value.DbValueType];
                        return stdValueHandler.BuildDisplayValue(value);
                    }

                    return null;
                });

            var segments =
                CategoryHelpers.SplitDisplayNameTemplateIntoSegments(template, replacements, wrappers);

            return segments;
        }

        public string BuildDisplayNameForResource(Resource resource, string template,
            (string Left, string Right)[] wrappers)
        {
            var segments = BuildDisplayNameSegmentsForResource(resource, template, wrappers);
            return string.Join("", segments.Select(a => a.Text));
        }

        public async Task<List<CategoryResourceDisplayNameViewModel>> PreviewResourceDisplayNameTemplate(int id,
            string template,
            int maxCount = 100)
        {
            var resourcesSearchResult = await ResourceService.Search(new ResourceSearchDto
            {
                Group = new ResourceSearchFilterGroup
                {
                    Combinator = Combinator.And, Filters =
                    [
                        new ResourceSearchFilter
                        {
                            IsCustomProperty = false,
                            Operation = SearchOperation.In,
                            PropertyId = (int) ResourceProperty.Category,
                            DbValue = new[] {id}.ToJson()
                        }
                    ]
                },
                // Orders = [new ResourceSearchOrderInputModel
                //             {
                //                 Asc = false,
                // 	Property = 
                //             }]
                PageIndex = 0,
                PageSize = maxCount
            }, false, true);

            var resources = resourcesSearchResult.Data ?? [];

            var wrapperPairs = (await SpecialTextService.GetAll(x => x.Type == SpecialTextType.Wrapper))
                .GroupBy(d => d.Value1)
                .Select(d => (Left: d.Key,
                    Rights: d.Select(c => c.Value2).Where(s => !string.IsNullOrEmpty(s)).Distinct().OfType<string>()
                        .First()))
                .OrderByDescending(d => d.Left.Length)
                .ToArray();

            var result = new List<CategoryResourceDisplayNameViewModel>();

            foreach (var r in resources)
            {
                // var replacements = matcherPropertyMap.ToDictionary(d => d.Key,
                //     d =>
                //     {
                //         var value = r.CustomPropertyValues?.FirstOrDefault(a => a?.PropertyId == d.Value.Id);
                //         if (value != null)
                //         {
                //             var displayValue = CustomPropertyDescriptorMap[d.Value.Type]
                //                 .ConvertDbValueToBizValue(d.Value, value);
                //             var stdValueHandler = StandardValueHandlerMap[d.Value.DbValueType];
                //             return stdValueHandler.BuildDisplayValue(displayValue);
                //         }
                //
                //         return null;
                //     });
                //
                // var segments =
                //     CategoryHelpers.SplitDisplayNameTemplateIntoSegments(template, replacements, wrapperPairs);
                var segments = BuildDisplayNameSegmentsForResource(r, template, wrapperPairs);

                result.Add(new CategoryResourceDisplayNameViewModel
                {
                    ResourceId = r.Id,
                    ResourcePath = r.Path,
                    Segments = segments
                });
            }

            return result;
        }

        public async Task<List<Abstractions.Models.Domain.Category>> GetByKeys(IEnumerable<int> keys,
            CategoryAdditionalItem additionalItems)
        {
            var data = await orm.GetByKeys(keys);
            var doList = data.Select(d => d.ToDomainModel()).ToArray();
            await Populate(doList, additionalItems);
            return doList.ToList();
        }
    }
}