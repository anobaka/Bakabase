using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Bakabase.Modules.Property.Services
{
    public abstract class AbstractCustomPropertyService<TDbContext>(IServiceProvider serviceProvider)
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

        public async Task<List<CustomProperty>> GetAll(
            Expression<Func<CustomPropertyDbModel, bool>>? selector = null,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
            bool returnCopy = true)
        {
            var dbData = await GetAll(selector, returnCopy);
            var data = dbData.Select(d => d.ToDomainModel()).ToList();
            await PopulateAdditionalItems(data, additionalItems);
            return data;
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
                            var propertyIds = properties.Select(x => x.Id).ToHashSet();
                            var values =
                                await CustomPropertyValueService.GetAllDbModels(x =>
                                    propertyIds.Contains(x.PropertyId));
                            var valueCountMap = values.GroupBy(v => v.PropertyId)
                                .ToDictionary(d => d.Key, d => d.Count());
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
            var propertyDescriptor = PropertyInternals.DescriptorMap[property.Type];

            var toBizValueType = toType.GetBizValueType();

            var fromStdHandler = StandardValueInternals.HandlerMap[property.Type.GetBizValueType()];
            var toStdHandler = StandardValueInternals.HandlerMap[toBizValueType];

            var bizValues = values.Select(v => propertyDescriptor.GetBizValue(property.ToProperty(), v.Value))
                .ToList();

            var result = new CustomPropertyTypeConversionPreviewViewModel
            {
                DataCount = bizValues.Count,
                FromType = property.Type.GetBizValueType(),
                ToType = toBizValueType,
                Changes = [],
            };

            foreach (var bizValue in bizValues)
            {
                var serializedFromBizValue = bizValue?.SerializeAsStandardValue(propertyDescriptor.BizValueType);
                var newBizValue = await StandardValueService.Convert(bizValue, property.Type.GetBizValueType(), toBizValueType);
                var serializedToBizValue = newBizValue?.SerializeAsStandardValue(toBizValueType);
                var fromDisplayValue = fromStdHandler.BuildDisplayValue(bizValue);
                var toDisplayValue = toStdHandler.BuildDisplayValue(newBizValue);
                if (fromDisplayValue != toDisplayValue)
                {
                    result.Changes.Add(
                        new CustomPropertyTypeConversionPreviewViewModel.Change(serializedFromBizValue,
                            serializedToBizValue));
                }
            }

            return result;
        }

        /// <summary>
        /// todo: factor
        /// </summary>
        /// <param name="sourcePropertyId"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public async Task<BaseResponse> ChangeType(int sourcePropertyId, PropertyType type)
        {
            var property = (await GetByKey(sourcePropertyId)).ToProperty();
            var values = await CustomPropertyValueService.GetAll(x => x.PropertyId == sourcePropertyId,
                CustomPropertyValueAdditionalItem.None, false);
            var propertyDescriptor = PropertyInternals.DescriptorMap[property.Type];
            var targetPropertyDescriptor = PropertyInternals.DescriptorMap[type];

            // var allowAddingNewDataDynamically =
            //     (property.Options as IAllowAddingNewDataDynamically)?.AllowAddingNewDataDynamically ?? false;
            var options = property.Options;

            var fakeNewProperty = property with
            {
                Type = type,
                Options = targetPropertyDescriptor.InitializeOptions(),
            };

            // clear previous options
            // property.Options = null;
            // brutal new property conversion.
            // var fakeNewProperty =
            //     (JsonConvert.DeserializeObject(JsonConvert.SerializeObject(property),
            //         targetPropertyDescriptor.PropertyType) as CustomProperty)!;
            // fakeNewProperty.Type = type;
            // fakeNewProperty.BizValueType = targetPropertyDescriptor.BizValueType;
            // fakeNewProperty.DbValueType = targetPropertyDescriptor.DbValueType;
            // fakeNewProperty.SetAllowAddingNewDataDynamically(true);

            var newValues = new List<CustomPropertyValue>();
            property.Options = options;

            foreach (var v in values)
            {
                var bizValue = propertyDescriptor.GetBizValue(property, v.Value);
                var nv = await StandardValueService.Convert(bizValue, property.Type.GetBizValueType(),
                    fakeNewProperty.Type.GetBizValueType());
                var (dbValue, _) = targetPropertyDescriptor.PrepareDbValue(fakeNewProperty, nv);
                var newValue = new CustomPropertyValue
                {
                    BizValue = bizValue,
                    Id = v.Id,
                    Property = v.Property,
                    PropertyId = v.PropertyId,
                    ResourceId = v.ResourceId,
                    Scope = v.Scope,
                    Value = dbValue
                };
                newValues.Add(newValue);
            }

            // roll back some options
            // fakeNewProperty.SetAllowAddingNewDataDynamically(allowAddingNewDataDynamically);
            await Put(fakeNewProperty.ToCustomProperty());
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