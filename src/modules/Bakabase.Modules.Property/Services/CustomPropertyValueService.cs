using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Property.Services
{
    public class
        CustomPropertyValueService<TDbContext>(
            IServiceProvider serviceProvider,
            IPropertyLocalizer localizer,
            IStandardValueLocalizer standardValueLocalizer,
            IStandardValueService standardValueService)
        : FullMemoryCacheResourceService<TDbContext, CustomPropertyValueDbModel, int>(
            serviceProvider), ICustomPropertyValueService where TDbContext : DbContext
    {
        protected ICustomPropertyService CustomPropertyService => GetRequiredService<ICustomPropertyService>();
        // private static readonly ConcurrentDictionary<int, object?> DbValueCache = new();

        public async Task<List<CustomPropertyValue>> GetAll(
            Expression<Func<CustomPropertyValueDbModel, bool>>? exp,
            CustomPropertyValueAdditionalItem additionalItems, bool returnCopy)
        {
            var data = await GetAll(exp, returnCopy);
            return await ToDomainModels(data, additionalItems, returnCopy);
        }

        public Task<List<CustomPropertyValueDbModel>> GetAllDbModels(
            Expression<Func<CustomPropertyValueDbModel, bool>>? selector = null,
            bool returnCopy = true) =>
            base.GetAll(selector, returnCopy);

        protected async Task<List<CustomPropertyValue>> ToDomainModels(
            List<CustomPropertyValueDbModel> values,
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
            var dtoList = values.Select(v => v.ToDomainModel(propertyMap[v.PropertyId].Type)).ToList();

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
                                    if (PropertyInternals.DescriptorMap.TryGetValue(p.Type, out var cpd))
                                    {
                                        dto.BizValue = cpd.GetBizValue(p.ToProperty(), dto.Value);
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
                customPropertyValues.ToDictionary(v => v.ToDbModel(propertyMap[v.PropertyId].Type.GetDbValueType())!,
                    v => v);
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
            var dbModels = customPropertyValues
                .Select(v => v.ToDbModel(propertyMap[v.PropertyId].Type.GetDbValueType())!)
                .ToList();
            await UpdateRange(dbModels);
            // foreach (var v in customPropertyValues)
            // {
            //     DbValueCache[v.Id] = v.Value;
            // }

            return BaseResponseBuilder.Ok;
        }

        public async Task<SingletonResponse<CustomPropertyValueDbModel>> AddDbModel(
            CustomPropertyValueDbModel resource)
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

        public async Task<BaseResponse> UpdateDbModel(CustomPropertyValueDbModel resource)
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
                data.ToDictionary(d => d.Id, d => d.Properties?.GetValueOrDefault((int) PropertyPool.Custom))
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
            var valuesToAdd = new List<CustomPropertyValueDbModel>();
            var valuesToUpdate = new List<CustomPropertyValueDbModel>();
            var changedProperties = new HashSet<CustomProperty>();

            // var newValuesDbValuesMap = new Dictionary<Bakabase.Abstractions.Models.Db.CustomPropertyValue, object?>();
            // var existedValuesDbValuesMap =
            // new Dictionary<Bakabase.Abstractions.Models.Db.CustomPropertyValue, object?>();

            foreach (var (resourceId, propertyValues) in resourceProperties)
            {
                foreach (var (propertyId, propertyValue) in propertyValues)
                {
                    var cp = propertyMap.GetValueOrDefault(propertyId);
                    if (cp != null)
                    {
                        if (!PropertyInternals.DescriptorMap.TryGetValue(cp.Type, out var pd))
                        {
                            Logger.LogError(localizer.DescriptorNotDefined(cp.Type));
                            continue;
                        }

                        var p = cp.ToProperty();

                        if (propertyValue.Values != null)
                        {
                            foreach (var v in propertyValue.Values)
                            {
                                var optimizedBizValue = StandardValueInternals
                                    .HandlerMap[cp.Type.GetBizValueType()].Optimize(v.BizValue);
                                var (rawDbValue, propertyChanged) = pd.PrepareDbValue(p, optimizedBizValue);

                                var dbPv = dbValueMap.GetValueOrDefault(resourceId)?.GetValueOrDefault(propertyId)
                                    ?.GetValueOrDefault(v.Scope);
                                if (dbPv == null)
                                {
                                    var pv = cp.Type.InitializeCustomPropertyValue(rawDbValue, resourceId,
                                        propertyId, v.Scope);
                                    var t = pv.ToDbModel(cp.Type.GetDbValueType())!;
                                    valuesToAdd.Add(t);
                                    // newValuesDbValuesMap[t] = rawDbValue;
                                }
                                else
                                {
                                    dbPv.Value = rawDbValue?.SerializeAsStandardValue(cp.Type.GetDbValueType());
                                    valuesToUpdate.Add(dbPv);
                                    // existedValuesDbValuesMap[dbPv] = rawDbValue;
                                }

                                if (propertyChanged)
                                {
                                    cp.Options = p.Options;
                                    changedProperties.Add(cp);
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
            StandardValueType bizValueType, CustomProperty customProperty, int resourceId,
            int scope)
        {
            if (!PropertyInternals.DescriptorMap.TryGetValue(customProperty.Type, out var pd))
            {
                Logger.LogError(localizer.DescriptorNotDefined(customProperty.Type));
                return null;
            }

            var property = customProperty.ToProperty();

            var (dbInnerValue, propertyChanged) = pd.PrepareDbValue(property, bizValue);
            if (propertyChanged)
            {
                customProperty.Options = property.Options;
            }

            var pv = customProperty.Type.InitializeCustomPropertyValue(dbInnerValue, resourceId,
                customProperty.Id, scope);

            return (pv, propertyChanged);
        }
    }
}