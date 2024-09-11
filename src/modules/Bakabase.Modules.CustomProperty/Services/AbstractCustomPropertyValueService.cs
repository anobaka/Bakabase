using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Helpers;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Bakabase.Modules.CustomProperty.Services
{
    public abstract class
        AbstractCustomPropertyValueService<TDbContext>(
            IServiceProvider serviceProvider,
            IEnumerable<IStandardValueHandler> converters,
            ICustomPropertyDescriptors propertyDescriptors,
            ICustomPropertyLocalizer localizer,
            IReservedPropertyValueService _reservedPropertyValueService,
            IStandardValueLocalizer standardValueLocalizer)
        : FullMemoryCacheResourceService<TDbContext, Bakabase.Abstractions.Models.Db.CustomPropertyValue, int>(
            serviceProvider), ICustomPropertyValueService where TDbContext : DbContext
    {
        protected ICustomPropertyService CustomPropertyService => GetRequiredService<ICustomPropertyService>();
        // private static readonly ConcurrentDictionary<int, object?> DbValueCache = new();

        private readonly Dictionary<StandardValueType, IStandardValueHandler> _converters =
            converters.ToDictionary(d => d.Type, d => d);

        public async Task<List<CustomPropertyValue>> GetAll(
            Expression<Func<Bakabase.Abstractions.Models.Db.CustomPropertyValue, bool>>? exp,
            CustomPropertyValueAdditionalItem additionalItems, bool returnCopy)
        {
            var data = await GetAll(exp, returnCopy);
            return await ToDomainModels(data, additionalItems, returnCopy);
        }

        public Task<List<Bakabase.Abstractions.Models.Db.CustomPropertyValue>> GetAllDbModels(
            Expression<Func<Bakabase.Abstractions.Models.Db.CustomPropertyValue, bool>>? selector = null,
            bool returnCopy = true) =>
            base.GetAll(selector, returnCopy);

        protected async Task<List<Bakabase.Abstractions.Models.Domain.CustomPropertyValue>> ToDomainModels(
            List<Bakabase.Abstractions.Models.Db.CustomPropertyValue> values,
            CustomPropertyValueAdditionalItem additionalItems, bool returnCopy)
        {
            var propertyIds = values.Select(v => v.PropertyId).ToHashSet();
            var properties =
                await CustomPropertyService.GetAll(x => propertyIds.Contains(x.Id),
                    CustomPropertyAdditionalItem.None, returnCopy);
            var propertyMap = properties.ToDictionary(x => x.Id);
            // var dtoList = values.Select(v => new CustomPropertyValue
            // {
            //     Id = v.Id, 
            //     Property = propertyMap[v.PropertyId], 
            //     PropertyId = v.PropertyId, 
            //     ResourceId = v.ResourceId,
            //     Scope = v.Scope,
            //     Value = DictionaryExtensions.GetOrAdd(DbValueCache, v.Id,
            //         () => v.Value?.DeserializeAsStandardValue(propertyMap[v.PropertyId].DbValueType))
            // }).ToList();
            var dtoList = values.Select(v => propertyDescriptors[propertyMap[v.PropertyId].Type].ToDomainModel(v))
                .ToList();

            foreach (var ai in SpecificEnumUtils<CustomPropertyValueAdditionalItem>.Values)
            {
                if (additionalItems.HasFlag(ai))
                {
                    switch (ai)
                    {
                        case CustomPropertyValueAdditionalItem.None:
                            break;
                        case CustomPropertyValueAdditionalItem.BizValue:
                        {
                            foreach (var dto in dtoList)
                            {
                                if (propertyMap.TryGetValue(dto.PropertyId, out var p))
                                {
                                    if (propertyDescriptors.TryGet(p.Type, out var cpd))
                                    {
                                        dto.BizValue = cpd.ConvertDbValueToBizValue(p, dto.Value);
                                    }
                                }
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            foreach (var dto in dtoList)
            {
                dto.Property = propertyMap[dto.PropertyId];
            }

            return dtoList;
        }

        public async Task<BaseResponse> AddRange(IEnumerable<CustomPropertyValue> values)
        {
            var customPropertyValues = values as CustomPropertyValue[] ?? values.ToArray();
            var pIds = customPropertyValues.Select(v => v.PropertyId).ToHashSet();
            var propertyMap = (await CustomPropertyService.GetByKeys(pIds)).ToDictionary(d => d.Id, d => d);
            var dbModelsMap =
                customPropertyValues.ToDictionary(v => v.ToDbModel(propertyMap[v.PropertyId].DbValueType)!, v => v);
            await AddRange(dbModelsMap.Keys.ToList());
            // foreach (var (k, v) in dbModelsMap)
            // {
            //     v.Id = k.Id;
            //     DbValueCache[v.Id] = v.Value;
            // }

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> UpdateRange(IEnumerable<CustomPropertyValue> values)
        {
            var customPropertyValues = values as CustomPropertyValue[] ?? values.ToArray();
            var pIds = customPropertyValues.Select(v => v.PropertyId).ToHashSet();
            var propertyMap = (await CustomPropertyService.GetByKeys(pIds)).ToDictionary(d => d.Id, d => d);
            var dbModels = customPropertyValues.Select(v => v.ToDbModel(propertyMap[v.PropertyId].DbValueType)!)
                .ToList();
            await UpdateRange(dbModels);
            // foreach (var v in customPropertyValues)
            // {
            //     DbValueCache[v.Id] = v.Value;
            // }

            return BaseResponseBuilder.Ok;
        }

        public async Task<SingletonResponse<Bakabase.Abstractions.Models.Db.CustomPropertyValue>> AddDbModel(
            Bakabase.Abstractions.Models.Db.CustomPropertyValue resource)
        {
            var rsp = await base.Add(resource);
            if (rsp.Code != (int) ResponseCode.Success)
            {
                return rsp;
            }

            // var property = await CustomPropertyService.GetByKey(resource.PropertyId);
            // DbValueCache[rsp.Data!.Id] = rsp.Data.Value?.DeserializeAsStandardValue(property.DbValueType);

            return rsp;
        }

        public async Task<BaseResponse> UpdateDbModel(Bakabase.Abstractions.Models.Db.CustomPropertyValue resource)
        {
            var rsp = await base.Update(resource);
            if (rsp.Code != (int) ResponseCode.Success)
            {
                return rsp;
            }

            // var property = await CustomPropertyService.GetByKey(resource.PropertyId);
            // DbValueCache[resource.Id] = resource.Value?.DeserializeAsStandardValue(property.DbValueType);

            return rsp;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task SaveByResources(List<Resource> data)
        {
            var resourceProperties =
                data.ToDictionary(d => d.Id, d => d.Properties?.GetValueOrDefault((int) ResourcePropertyType.Custom))
                    .Where(d => d.Value != null).ToDictionary(d => d.Key, d => d.Value!);

            var propertyIds = resourceProperties.SelectMany(d => d.Value.Select(c => c.Key)).ToHashSet();
            var properties = await CustomPropertyService.GetByKeys(propertyIds, CustomPropertyAdditionalItem.None);
            var propertyMap = properties.ToDictionary(d => d.Id, d => d);

            // ResourceId - PropertyId - Scope - Value
            var resourceIds = resourceProperties.Keys.ToList();
            var dbValueMap =
                (await GetAll(x => resourceIds.Contains(x.ResourceId)))
                .GroupBy(d => d.ResourceId)
                .ToDictionary(d => d.Key,
                    d => d.GroupBy(x => x.PropertyId)
                        .ToDictionary(c => c.Key, c => c.ToDictionary(e => e.Scope, e => e)));
            var valuesToAdd = new List<Bakabase.Abstractions.Models.Db.CustomPropertyValue>();
            var valuesToUpdate = new List<Bakabase.Abstractions.Models.Db.CustomPropertyValue>();
            var changedProperties = new HashSet<Bakabase.Abstractions.Models.Domain.CustomProperty>();

            // var newValuesDbValuesMap = new Dictionary<Bakabase.Abstractions.Models.Db.CustomPropertyValue, object?>();
            // var existedValuesDbValuesMap =
            // new Dictionary<Bakabase.Abstractions.Models.Db.CustomPropertyValue, object?>();

            foreach (var (resourceId, propertyValues) in resourceProperties)
            {
                foreach (var (propertyId, propertyValue) in propertyValues)
                {
                    var property = propertyMap.GetValueOrDefault(propertyId);
                    if (property != null)
                    {
                        if (!propertyDescriptors.TryGet(property.Type, out var pd))
                        {
                            Logger.LogError(localizer.CustomProperty_DescriptorNotFound(property.Type));
                            continue;
                        }

                        if (propertyValue.Values != null)
                        {
                            foreach (var v in propertyValue.Values)
                            {
                                var (rawDbValue, propertyChanged) = pd.PrepareDbValueFromBizValue(property, v.BizValue);

                                var dbPv = dbValueMap.GetValueOrDefault(resourceId)?.GetValueOrDefault(propertyId)
                                    ?.GetValueOrDefault(v.Scope);
                                if (dbPv == null)
                                {
                                    var pv = CustomPropertyValueHelper.CreateFromImplicitValue(rawDbValue,
                                        property.Type, resourceId, propertyId, v.Scope);
                                    var t = pv.ToDbModel(property.DbValueType)!;
                                    valuesToAdd.Add(t);
                                    // newValuesDbValuesMap[t] = rawDbValue;
                                }
                                else
                                {
                                    dbPv.Value = rawDbValue?.SerializeAsStandardValue(property.DbValueType);
                                    valuesToUpdate.Add(dbPv);
                                    // existedValuesDbValuesMap[dbPv] = rawDbValue;
                                }

                                if (propertyChanged)
                                {
                                    changedProperties.Add(property);
                                }
                            }
                        }
                    }
                }
            }

            await CustomPropertyService.UpdateRange(changedProperties.Select(p => p.ToDbModel()!).ToList());
            var added = await AddRange(valuesToAdd);
            await UpdateRange(valuesToUpdate);

            // if (added.Data != null)
            // {
            //     for (var i = 0; i < added.Data.Count; i++)
            //     {
            //         DbValueCache[added.Data[i].Id] = newValuesDbValuesMap[valuesToAdd[i]];
            //     }
            // }
            //
            // foreach (var t in valuesToUpdate)
            // {
            //     DbValueCache[t.Id] = existedValuesDbValuesMap[t];
            // }
        }

        public async Task<(CustomPropertyValue Value, bool PropertyChanged)?> CreateTransient(object? bizValue,
            StandardValueType bizValueType, Bakabase.Abstractions.Models.Domain.CustomProperty property, int resourceId,
            int scope)
        {
            if (!propertyDescriptors.TryGet(property.Type, out var pd))
            {
                Logger.LogError(localizer.CustomProperty_DescriptorNotFound(property.Type));
                return null;
            }

            var stdValueConverter = _converters.GetValueOrDefault(bizValueType);
            if (stdValueConverter == null)
            {
                Logger.LogError(standardValueLocalizer.StandardValue_HandlerNotFound(bizValueType));
                return null;
            }

            var (dbInnerValue, propertyChanged) = pd.PrepareDbValueFromBizValue(property, bizValue);

            var pv = CustomPropertyValueHelper.CreateFromImplicitValue(dbInnerValue, property.Type, resourceId,
                property.Id, scope);

            return (pv, propertyChanged);
        }
    }
}