using System.Linq.Expressions;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Abstractions.Components;
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
            IStandardValueService standardValueService,
            IResourceDataChangeEventPublisher eventPublisher,
            ILogger<CustomPropertyValueService<TDbContext>> logger)
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

        /// <summary>
        /// Efficiently count values by property IDs using in-memory cache
        /// </summary>
        public async Task<Dictionary<int, int>> GetCountByPropertyIds(IEnumerable<int> propertyIds)
        {
            var propertyIdSet = propertyIds.ToHashSet();
            var allValues = await GetAllDbModels(v => propertyIdSet.Contains(v.PropertyId), false);
            return allValues.GroupBy(v => v.PropertyId)
                .ToDictionary(g => g.Key, g => g.Count());
        }

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
                                    var cpd = PropertySystem.Property.TryGetDescriptor(p.Type);
                                    if (cpd != null)
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
            eventPublisher.PublishResourcesChanged(customPropertyValues.Select(v => v.ResourceId));

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
            eventPublisher.PublishResourcesChanged(customPropertyValues.Select(v => v.ResourceId));

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

            eventPublisher.PublishResourceChanged(resource.ResourceId);

            return rsp;
        }

        public async Task<BaseResponse> UpdateDbModel(CustomPropertyValueDbModel resource)
        {
            var rsp = await base.Update(resource);
            if (rsp.Code != (int) ResponseCode.Success)
            {
                return rsp;
            }

            eventPublisher.PublishResourceChanged(resource.ResourceId);

            return rsp;
        }

        public async Task<BaseResponse> AddDbModelRange(IEnumerable<CustomPropertyValueDbModel> resources)
        {
            var list = resources.ToList();
            if (list.Count == 0) return BaseResponseBuilder.Ok;

            await AddRange(list);
            eventPublisher.PublishResourcesChanged(list.Select(r => r.ResourceId).Distinct());

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> UpdateDbModelRange(IEnumerable<CustomPropertyValueDbModel> resources)
        {
            var list = resources.ToList();
            if (list.Count == 0) return BaseResponseBuilder.Ok;

            // Optimization: Only update records that actually changed
            // Reading from cache is lock-free, writing has lock - so minimize writes
            var ids = list.Select(r => r.Id).ToList();
            var existingValues = await GetByKeys(ids, asNoTracking: true);
            var existingMap = existingValues.ToDictionary(e => e.Id, e => e);

            var changedList = list.Where(newVal =>
            {
                if (!existingMap.TryGetValue(newVal.Id, out var existing))
                    return true; // Not found in cache, should update (shouldn't happen but safe)
                // Compare the Value field - if same, skip update
                return existing.Value != newVal.Value;
            }).ToList();

            var skippedCount = list.Count - changedList.Count;
            if (skippedCount > 0)
            {
                logger.LogInformation("[PropertyValue] Skipped {SkippedCount}/{TotalCount} unchanged updates",
                    skippedCount, list.Count);
            }

            if (changedList.Count == 0) return BaseResponseBuilder.Ok;

            await UpdateRange(changedList);
            eventPublisher.PublishResourcesChanged(changedList.Select(r => r.ResourceId).Distinct());

            return BaseResponseBuilder.Ok;
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
                        var pd = PropertySystem.Property.TryGetDescriptor(cp.Type);
                        if (pd == null)
                        {
                            Logger.LogError(localizer.DescriptorNotDefined(cp.Type));
                            continue;
                        }

                        var p = cp.ToProperty();

                        if (propertyValue.Values != null)
                        {
                            foreach (var v in propertyValue.Values)
                            {
                                var optimizedBizValue = StandardValueSystem
                                    .GetHandler(cp.Type.GetBizValueType()).Optimize(v.BizValue);
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

            var changedResourceIds = valuesToAdd.Select(v => v.ResourceId)
                .Concat(valuesToUpdate.Select(v => v.ResourceId))
                .Distinct()
                .ToList();
            if (changedResourceIds.Count > 0)
            {
                eventPublisher.PublishResourcesChanged(changedResourceIds);
            }
        }

        public async Task<(CustomPropertyValue Value, bool PropertyChanged)?> CreateTransient(object? bizValue,
            StandardValueType bizValueType, CustomProperty customProperty, int resourceId,
            int scope)
        {
            var pd = PropertySystem.Property.TryGetDescriptor(customProperty.Type);
            if (pd == null)
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