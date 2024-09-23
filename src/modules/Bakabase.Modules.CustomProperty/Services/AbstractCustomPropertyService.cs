using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Components.Properties.Choice;
using Bakabase.Modules.CustomProperty.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.CustomProperty.Components.Properties.Multilevel;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Models.View;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Services
{
    public abstract class AbstractCustomPropertyService<TDbContext>(
        IServiceProvider serviceProvider,
        ICustomPropertyDescriptors propertyDescriptors)
        : FullMemoryCacheResourceService<TDbContext, Bakabase.Abstractions.Models.Db.CustomProperty, int>(
                serviceProvider),
            ICustomPropertyService where TDbContext : DbContext
    {
        protected ICategoryCustomPropertyMappingService CategoryCustomPropertyMappingService =>
            GetRequiredService<ICategoryCustomPropertyMappingService>();

        protected ICustomPropertyValueService CustomPropertyValueService =>
            GetRequiredService<ICustomPropertyValueService>();

        protected IStandardValueService StandardValueService => GetRequiredService<IStandardValueService>();
        protected ICategoryService CategoryService => GetRequiredService<ICategoryService>();

        protected Dictionary<StandardValueType, IStandardValueHandler> StdValueHandlers =>
            GetRequiredService<IEnumerable<IStandardValueHandler>>().ToDictionary(d => d.Type, d => d);

        public async Task<List<Abstractions.Models.CustomProperty>> GetAll(
            Expression<Func<Bakabase.Abstractions.Models.Db.CustomProperty, bool>>? selector = null,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
            bool returnCopy = true)
        {
            var data = await GetAll(selector, returnCopy);
            var dtoList = await ToDomainModels(data, additionalItems);
            return dtoList;
        }

        public async Task<Abstractions.Models.CustomProperty> GetByKey(int id,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
            bool returnCopy = true)
        {
            var data = await base.GetByKey(id, returnCopy);
            var dtoList = await ToDomainModels([data], additionalItems);
            return dtoList.First();
        }

        public async Task<List<Abstractions.Models.CustomProperty>> GetByKeys(IEnumerable<int> ids,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
            bool returnCopy = true)
        {
            var data = await base.GetByKeys(ids, returnCopy);
            var dtoList = await ToDomainModels(data.ToList(), additionalItems);
            return dtoList;
        }

        public async Task<Dictionary<int, List<Abstractions.Models.CustomProperty>>>
            GetByCategoryIds(int[] ids)
        {
            var mappings = await CategoryCustomPropertyMappingService.GetAll(x => ids.Contains(x.CategoryId));
            var propertyIds = mappings.Select(x => x.PropertyId).ToHashSet();
            var properties = await GetAll(x => propertyIds.Contains(x.Id));
            var propertyMap = properties.ToDictionary(x => x.Id);

            return mappings.GroupBy(x => x.CategoryId).ToDictionary(x => x.Key,
                x => x.Select(y => propertyMap.GetValueOrDefault(y.PropertyId)).Where(y => y != null).ToList())!;
        }

        private Abstractions.Models.CustomProperty? ToDomainModel(
            Bakabase.Abstractions.Models.Db.CustomProperty? property) =>
            property == null ? null : propertyDescriptors[property.Type].ToDomainModel(property);

        private async Task<List<Abstractions.Models.CustomProperty>> ToDomainModels(
            List<Bakabase.Abstractions.Models.Db.CustomProperty> properties,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
        {
            var dtoList = properties.Select(p => ToDomainModel(p)!).ToList();
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

                            foreach (var dto in dtoList)
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
                            foreach (var d in dtoList)
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

            return dtoList;
        }

        public async Task<Abstractions.Models.CustomProperty> Add(CustomPropertyAddOrPutDto model)
        {
            var data = await Add(new Bakabase.Abstractions.Models.Db.CustomProperty
            {
                CreatedAt = DateTime.Now,
                Name = model.Name,
                Options = model.Options,
                Type = model.Type
            });

            return ToDomainModel(data.Data)!;
        }

        public async Task<List<Abstractions.Models.CustomProperty>> AddRange(CustomPropertyAddOrPutDto[] models)
        {
            var now = DateTime.Now;
            var data = await AddRange(models.Select(model => new Bakabase.Abstractions.Models.Db.CustomProperty
            {
                CreatedAt = now,
                Name = model.Name,
                Options = model.Options,
                Type = model.Type
            }).ToList());
            return data.Data.Select(d => ToDomainModel(d)!).ToList();
        }

        public async Task<Abstractions.Models.CustomProperty> Put(int id, CustomPropertyAddOrPutDto model)
        {
            var rsp = await UpdateByKey(id, cp =>
            {
                cp.Name = model.Name;
                cp.Options = model.Options;
                cp.Type = model.Type;
            });

            return ToDomainModel(rsp.Data)!;
        }

        public override async Task<BaseResponse> RemoveByKey(int id)
        {
            await CategoryCustomPropertyMappingService.RemoveAll(x => x.PropertyId == id);
            await CustomPropertyValueService.RemoveAll(x => x.PropertyId == id);
            return await base.RemoveByKey(id);
        }

        public async Task<CustomPropertyTypeConversionPreviewViewModel> PreviewTypeConversion(int sourcePropertyId,
            CustomPropertyType toType)
        {
            var property = await GetByKey(sourcePropertyId);
            var values = await CustomPropertyValueService.GetAll(x => x.PropertyId == sourcePropertyId,
                CustomPropertyValueAdditionalItem.None, false);
            var propertyDescriptor = propertyDescriptors[property.Type];

            var toBizValueType = toType.GetBizValueType();

            var fromStdHandler = StdValueHandlers[property.BizValueType];
            var toStdHandler = StdValueHandlers[toBizValueType];

            var bizValues = values.Select(v => propertyDescriptor.ConvertDbValueToBizValue(property, v.Value))
                .ToList();

            var result = new CustomPropertyTypeConversionPreviewViewModel
            {
                DataCount = bizValues.Count,
                FromType = property.BizValueType,
                ToType = toBizValueType,
                Changes = [],
            };

            foreach (var bizValue in bizValues)
            {
                var serializedFromBizValue = bizValue?.SerializeAsStandardValue(propertyDescriptor.BizValueType);
                var newBizValue = await StandardValueService.Convert(bizValue, property.BizValueType, toBizValueType);
                var serializedToBizValue = newBizValue?.SerializeAsStandardValue(toBizValueType);
                var fromDisplayValue = fromStdHandler.BuildDisplayValue(bizValue);
                var toDisplayValue = toStdHandler.BuildDisplayValue(newBizValue);
                if (fromDisplayValue != toDisplayValue)
                {
                    result.Changes.Add(new CustomPropertyTypeConversionPreviewViewModel.Change(serializedFromBizValue, serializedToBizValue));
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
        public async Task<BaseResponse> ChangeType(int sourcePropertyId, CustomPropertyType type)
        {
            var property = await GetByKey(sourcePropertyId);
            var values = await CustomPropertyValueService.GetAll(x => x.PropertyId == sourcePropertyId,
                CustomPropertyValueAdditionalItem.None, false);
            var propertyDescriptor = propertyDescriptors[property.Type];
            var targetPropertyDescriptor = propertyDescriptors[(int)type];

            var allowAddingNewDataDynamically =
                (property.Options as IAllowAddingNewDataDynamically)?.AllowAddingNewDataDynamically ?? false;
            var options = property.Options;

            // clear previous options
            property.Options = null;
            // brutal new property conversion.
            var fakeNewProperty = (JsonConvert.DeserializeObject(JsonConvert.SerializeObject(property),
                targetPropertyDescriptor.PropertyType) as Abstractions.Models.CustomProperty)!;
            fakeNewProperty.Type = (int)type;
            fakeNewProperty.BizValueType = targetPropertyDescriptor.BizValueType;
            fakeNewProperty.DbValueType = targetPropertyDescriptor.DbValueType;
            fakeNewProperty.SetAllowAddingNewDataDynamically(true);

            var newValues = new List<CustomPropertyValue>();
            property.Options = options;

            foreach (var v in values)
            {
                var bizValue = propertyDescriptor.ConvertDbValueToBizValue(property, v.Value);
                var nv = await StandardValueService.Convert(bizValue, property.BizValueType, fakeNewProperty.BizValueType);
                var (dbValue, _) = targetPropertyDescriptor.PrepareDbValueFromBizValue(fakeNewProperty, nv);
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

            fakeNewProperty.SetAllowAddingNewDataDynamically(allowAddingNewDataDynamically);
            await Put(fakeNewProperty);
            await CustomPropertyValueService.UpdateRange(newValues);
            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> Put(Bakabase.Abstractions.Models.Domain.CustomProperty resource)
        {
            return await Update(resource.ToDbModel());
        }

        public async Task<BaseResponse> EnableAddingNewDataDynamically(int id)
        {
            var property = await GetByKey(id, CustomPropertyAdditionalItem.None);
            if (property.Options is IAllowAddingNewDataDynamically a)
            {
                a.AllowAddingNewDataDynamically = true;
            }

            await Put(id, new CustomPropertyAddOrPutDto
            {
                Name = property.Name,
                Options = JsonConvert.SerializeObject(property.Options),
                Type = property.Type
            });
            return BaseResponseBuilder.Ok;
        }
    }
}