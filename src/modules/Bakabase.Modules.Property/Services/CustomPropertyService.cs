using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StackExchange.Profiling;

namespace Bakabase.Modules.Property.Services
{
    public class CustomPropertyService<TDbContext>(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<TDbContext, CustomPropertyDbModel, int>(
                serviceProvider),
            ICustomPropertyService where TDbContext : DbContext
    {
        protected ICategoryCustomPropertyMappingService CategoryCustomPropertyMappingService =>
            GetRequiredService<ICategoryCustomPropertyMappingService>();

        protected ICustomPropertyValueService CustomPropertyValueService =>
            GetRequiredService<ICustomPropertyValueService>();

        protected IStandardValueService StandardValueService => GetRequiredService<IStandardValueService>();
        protected ICategoryService CategoryService => GetRequiredService<ICategoryService>();
        protected IPropertyTypeConverter PropertyTypeConverter => GetRequiredService<IPropertyTypeConverter>();

        public async Task<List<CustomProperty>> GetAll(
            Expression<Func<CustomPropertyDbModel, bool>>? selector = null,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
            bool returnCopy = true)
        {
            using (MiniProfiler.Current.Step("CustomPropertyService.GetAll"))
            {
                List<CustomPropertyDbModel> dbData;
                using (MiniProfiler.Current.Step("GetAll (DbModels from cache)"))
                {
                    dbData = await GetAll(selector, returnCopy);
                }

                List<CustomProperty> data;
                using (MiniProfiler.Current.Step($"ToDomainModel ({dbData.Count} items)"))
                {
                    data = dbData.ToDomainModelsBatch();
                }

                using (MiniProfiler.Current.Step($"PopulateAdditionalItems ({additionalItems})"))
                {
                    await PopulateAdditionalItems(data, additionalItems);
                }

                return data;
            }
        }

        public async Task<CustomProperty> GetByKey(int id,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None, bool returnCopy = true)
        {
            var dbData = await base.GetByKey(id, returnCopy);
            var data = dbData.ToDomainModel();
            await PopulateAdditionalItems([data], additionalItems);
            return data;
        }

        public async Task<List<CustomProperty>> GetByKeys(IEnumerable<int> ids,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None, bool returnCopy = true)
        {
            var dbData = await base.GetByKeys(ids, returnCopy);
            var data = dbData.Select(d => d.ToDomainModel()).ToList();
            await PopulateAdditionalItems(data, additionalItems);
            return data;
        }

        public async Task<Dictionary<int, List<CustomProperty>>> GetByCategoryIds(int[] ids)
        {
            var mappings = await CategoryCustomPropertyMappingService.GetAll(x => ids.Contains(x.CategoryId));
            var propertyIds = mappings.Select(x => x.PropertyId).ToHashSet();
            var properties = await GetAll(x => propertyIds.Contains(x.Id));
            var propertyMap = properties.ToDictionary(x => x.Id);
            var orderMap = mappings.GroupBy(d => d.CategoryId)
                .ToDictionary(d => d.Key, d => d.ToDictionary(a => a.PropertyId, a => a.Order));

            return mappings.GroupBy(x => x.CategoryId).ToDictionary(x => x.Key,
                x => x.Select(y => propertyMap.GetValueOrDefault(y.PropertyId)).OfType<CustomProperty>()
                    .OrderBy(a =>
                        orderMap.GetValueOrDefault(x.Key)?.GetValueOrDefault(a.Id, int.MaxValue) ?? int.MaxValue)
                    .ToList());
        }

        private async Task PopulateAdditionalItems(List<CustomProperty> properties,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
        {
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
                            var categories = await CategoryService.GetAll(x => categoryIds.Contains(x.Id));
                            var categoryMap = categories.ToDictionary(x => x.Id);
                            var propertyCategoryIdMap = mappings.GroupBy(x => x.PropertyId)
                                .ToDictionary(x => x.Key, x => x.Select(y => y.CategoryId).ToHashSet());

                            foreach (var dto in properties)
                            {
                                dto.Categories = propertyCategoryIdMap.GetValueOrDefault(dto.Id)
                                    ?.Select(x => categoryMap.GetValueOrDefault(x)).Where(x => x != null).ToList()!;
                            }

                            break;
                        }
                        case CustomPropertyAdditionalItem.ValueCount:
                        {
                            var propertyIds = properties.Select(x => x.Id).ToList();
                            var valueCountMap = await CustomPropertyValueService.GetCountByPropertyIds(propertyIds);
                            foreach (var d in properties)
                            {
                                d.ValueCount = valueCountMap.GetValueOrDefault(d.Id);
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
        }

        public async Task<CustomProperty> Add(CustomPropertyAddOrPutDto model)
        {
            var data = await Add(new CustomPropertyDbModel
            {
                CreatedAt = DateTime.Now,
                Name = model.Name,
                Options = model.Options,
                Type = model.Type
            });

            return data.Data!.ToDomainModel();
        }

        public async Task<List<CustomProperty>> AddRange(CustomPropertyAddOrPutDto[] models)
        {
            var now = DateTime.Now;
            var data = await AddRange(models.Select(model => new CustomPropertyDbModel()
            {
                CreatedAt = now,
                Name = model.Name,
                Options = model.Options,
                Type = model.Type
            }).ToList());
            return data.Data!.Select(d => d.ToDomainModel()).ToList();
        }

        public async Task<CustomProperty> Put(int id, CustomPropertyAddOrPutDto model)
        {
            var rsp = await UpdateByKey(id, cp =>
            {
                cp.Name = model.Name;
                cp.Options = model.Options;
                cp.Type = model.Type;
            });

            return rsp.Data!.ToDomainModel();
        }

        public async Task Sort(int[] ids)
        {
            var properties = await GetAll();
            var orderMap = new Dictionary<int, int>();
            for (var i = 0; i < ids.Length; i++)
            {
                orderMap[ids[i]] = i;
            }

            foreach (var property in properties)
            {
                if (orderMap.TryGetValue(property.Id, out var order))
                {
                    property.Order = order;
                }
            }

            await UpdateRange(properties.Select(x => x.ToDbModel()).ToList());
        }

        public override async Task<BaseResponse> RemoveByKey(int id)
        {
            await CategoryCustomPropertyMappingService.RemoveAll(x => x.PropertyId == id);
            await CustomPropertyValueService.RemoveAll(x => x.PropertyId == id);
            return await base.RemoveByKey(id);
        }

        public async Task<CustomPropertyTypeConversionPreviewViewModel> PreviewTypeConversion(int sourcePropertyId,
            PropertyType toType)
        {
            var property = await GetByKey(sourcePropertyId);
            var values = await CustomPropertyValueService.GetAll(x => x.PropertyId == sourcePropertyId,
                CustomPropertyValueAdditionalItem.None, false);

            var preview = await PropertyTypeConverter.PreviewConversionAsync(
                property.ToProperty(),
                toType,
                values.Select(v => v.Value));

            return new CustomPropertyTypeConversionPreviewViewModel
            {
                DataCount = preview.TotalCount,
                FromType = preview.FromBizType,
                ToType = preview.ToBizType,
                Changes = preview.Changes.Select(c =>
                    new CustomPropertyTypeConversionPreviewViewModel.Change(c.FromSerialized, c.ToSerialized)).ToList()
            };
        }

        /// <summary>
        /// Change the type of a custom property and convert all its values.
        /// Uses IPropertyTypeConverter for the actual conversion.
        /// </summary>
        public async Task<BaseResponse> ChangeType(int sourcePropertyId, PropertyType type)
        {
            var property = (await GetByKey(sourcePropertyId)).ToProperty();
            var values = await CustomPropertyValueService.GetAll(x => x.PropertyId == sourcePropertyId,
                CustomPropertyValueAdditionalItem.None, false);
            var targetPropertyDescriptor = PropertySystem.Property.GetDescriptor(type);

            // Create target property with initialized options
            var toProperty = property with
            {
                Type = type,
                Options = targetPropertyDescriptor.InitializeOptions(),
            };

            // Batch convert all values using the type converter
            var conversionResult = await PropertyTypeConverter.ConvertValuesAsync(
                property,
                toProperty,
                values.Select(v => v.Value));

            // Build new values with converted DB values
            var newValues = values.Select((v, i) => new CustomPropertyValue
            {
                Id = v.Id,
                Property = v.Property,
                PropertyId = v.PropertyId,
                ResourceId = v.ResourceId,
                Scope = v.Scope,
                Value = conversionResult.NewDbValues[i]
            }).ToList();

            // Update property and values
            await Put(conversionResult.UpdatedToProperty.ToCustomProperty());
            await CustomPropertyValueService.UpdateRange(newValues);
            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> Put(CustomProperty resource)
        {
            return await Update(resource.ToDbModel());
        }

        // public async Task<BaseResponse> EnableAddingNewDataDynamically(int id)
        // {
        //     var property = await GetByKey(id, CustomPropertyAdditionalItem.None);
        //     // if (property.Options is IAllowAddingNewDataDynamically a)
        //     // {
        //     //     a.AllowAddingNewDataDynamically = true;
        //     // }
        //
        //     await Put(id, new CustomPropertyAddOrPutDto
        //     {
        //         Name = property.Name,
        //         Options = JsonConvert.SerializeObject(property.Options),
        //         Type = property.Type
        //     });
        //     return BaseResponseBuilder.Ok;
        // }
    }
}