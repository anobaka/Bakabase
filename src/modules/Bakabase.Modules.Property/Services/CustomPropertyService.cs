using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Modules.StandardValue.Abstractions.Services;
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
        protected ICustomPropertyValueService CustomPropertyValueService =>
            GetRequiredService<ICustomPropertyValueService>();

        protected IStandardValueService StandardValueService => GetRequiredService<IStandardValueService>();
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
                            break;
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
                Options = NormalizeOptions(model.Type, model.Options),
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
                Options = NormalizeOptions(model.Type, model.Options),
                Type = model.Type
            }).ToList());
            return data.Data!.Select(d => d.ToDomainModel()).ToList();
        }

        public async Task<CustomProperty> Put(int id, CustomPropertyAddOrPutDto model)
        {
            var rsp = await UpdateByKey(id, cp =>
            {
                cp.Name = model.Name;
                cp.Options = NormalizeOptions(model.Type, model.Options, cp.Type == model.Type ? cp.Options : null);
                cp.Type = model.Type;
            });

            return rsp.Data!.ToDomainModel();
        }

        private static string? NormalizeOptions(PropertyType type, string? serializedOptions,
            string? previousSerializedOptions = null)
        {
            if (string.IsNullOrEmpty(serializedOptions)) return serializedOptions;
            var descriptor = PropertySystem.Property.GetDescriptor(type);
            if (!descriptor.IsReferenceValueType || descriptor.OptionsType == null) return serializedOptions;
            var options = JsonConvert.DeserializeObject(serializedOptions, descriptor.OptionsType);
            if (options is not IReferencePropertyOptions { IgnoreCase: true }) return serializedOptions;
            var previousOptions = string.IsNullOrEmpty(previousSerializedOptions)
                ? null
                : JsonConvert.DeserializeObject(previousSerializedOptions, descriptor.OptionsType);
            ReferencePropertyOptionsNormalizer.Normalize(options, previousOptions);
            return JsonConvert.SerializeObject(options);
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

        public async Task SetOrders(IReadOnlyDictionary<int, int> orders)
        {
            if (orders.Count == 0) return;
            // UpdateByKeys writes only the given rows, and only the column the lambda changes (unlike Sort).
            await UpdateByKeys(orders.Keys.ToArray(), cp => cp.Order = orders[cp.Id]);
        }

        public Task<List<CustomPropertyDbModel>> GetAllDbModels(
            Expression<Func<CustomPropertyDbModel, bool>>? selector = null, bool returnCopy = true) =>
            base.GetAll(selector, returnCopy);

        public override async Task<BaseResponse> RemoveByKey(int id)
        {
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

            if (property.Options is IReferencePropertyOptions sourceOptions &&
                toProperty.Options is IReferencePropertyOptions targetOptions)
            {
                targetOptions.IgnoreCase = sourceOptions.IgnoreCase;
            }

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

            // Update property and values. Only the type and options change: writing the whole row
            // from the converted property would reset CreatedAt and Order, which it does not carry.
            // The options are serialized before the row is touched: a failure inside the lambda would
            // leave the row tracked with only its type changed, for the scope's next save to write.
            var options =
                conversionResult.UpdatedToProperty.Options.SerializeAsCustomPropertyOptions(throwOnError: true);
            await UpdateByKey(sourcePropertyId, cp =>
            {
                cp.Type = type;
                cp.Options = options;
            });
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