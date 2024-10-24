using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Components;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Extensions;
using Humanizer.Localisation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using IEnhancer = Bakabase.Modules.Enhancer.Abstractions.Components.IEnhancer;

namespace Bakabase.InsideWorld.Business.Components.Enhancer
{
    public class EnhancerService : IEnhancerService
    {
        private readonly ICustomPropertyService _customPropertyService;
        private readonly IResourceService _resourceService;
        private readonly IReservedPropertyValueService _reservedPropertyValueService;
        private readonly ICustomPropertyValueService _customPropertyValueService;
        private readonly IEnhancementService _enhancementService;
        private readonly ICategoryEnhancerOptionsService _categoryEnhancerService;
        private readonly IStandardValueService _standardValueService;
        private readonly LogService _logService;
        private readonly IEnhancerDescriptors _enhancerDescriptors;
        private readonly IBakabaseLocalizer _bakabaseLocalizer;
        private readonly IEnhancerLocalizer _enhancerLocalizer;
        private readonly IPropertyLocalizer _propertyLocalizer;
        private readonly Dictionary<int, IEnhancer> _enhancers;
        private readonly ILogger<EnhancerService> _logger;
        private readonly ICategoryService _categoryService;
        private readonly IEnhancementRecordService _enhancementRecordService;
        private readonly IServiceProvider _serviceProvider;

        public EnhancerService(ICustomPropertyService customPropertyService, IResourceService resourceService,
            ICustomPropertyValueService customPropertyValueService,
            IEnhancementService enhancementService, ICategoryEnhancerOptionsService categoryEnhancerService,
            IStandardValueService standardValueService, LogService logService, IEnhancerLocalizer enhancerLocalizer,
            IEnhancerDescriptors enhancerDescriptors, IEnumerable<IEnhancer> enhancers, ILogger<EnhancerService> logger,
            ICategoryService categoryService, IEnhancementRecordService enhancementRecordService,
            IBakabaseLocalizer bakabaseLocalizer, IReservedPropertyValueService reservedPropertyValueService,
            IPropertyLocalizer propertyLocalizer, IServiceProvider serviceProvider)
        {
            _customPropertyService = customPropertyService;
            _resourceService = resourceService;
            _customPropertyValueService = customPropertyValueService;
            _enhancementService = enhancementService;
            _categoryEnhancerService = categoryEnhancerService;
            _standardValueService = standardValueService;
            _logService = logService;
            _enhancerLocalizer = enhancerLocalizer;
            _enhancerDescriptors = enhancerDescriptors;
            _logger = logger;
            _categoryService = categoryService;
            _enhancementRecordService = enhancementRecordService;
            _bakabaseLocalizer = bakabaseLocalizer;
            _reservedPropertyValueService = reservedPropertyValueService;
            _propertyLocalizer = propertyLocalizer;
            _serviceProvider = serviceProvider;
            _enhancers = enhancers.ToDictionary(d => d.Id, d => d);
        }

        protected async Task ApplyEnhancementsToResources(List<Enhancement> enhancements, CancellationToken ct)
        {
            var resourceIds = enhancements.Select(s => s.ResourceId).ToHashSet();
            var resourceMap = (await _resourceService.GetByKeys(resourceIds.ToArray())).ToDictionary(d => d.Id, d => d);
            var categoryIds = resourceMap.Values.Select(v => v.CategoryId).ToHashSet();
            var enhancerIds = enhancements.Select(s => s.EnhancerId).ToHashSet();
            var enhancerOptions = await _categoryEnhancerService.GetAll(x =>
                enhancerIds.Contains(x.EnhancerId) && categoryIds.Contains(x.CategoryId));
            // CategoryId - EnhancerId - Options
            var categoryEnhancerOptionsMap = enhancerOptions.GroupBy(d => d.CategoryId)
                .ToDictionary(d => d.Key, d => d.ToDictionary(c => c.EnhancerId, c => c));

            var propertyMap = (await _customPropertyService.GetAll()).ToDictionary(d => d.Id, d => d);
            var currentPropertyValues =
                await _customPropertyValueService.GetAll(x => resourceIds.Contains(x.ResourceId),
                    CustomPropertyValueAdditionalItem.None, true);
            var currentPropertyValuesMap = currentPropertyValues.ToDictionary(d => d.BizKey, d => d);

            var enhancementTargetOptionsMap = new Dictionary<Enhancement, EnhancerTargetFullOptions>();
            var changedCategoryEnhancerOptions = new HashSet<CategoryEnhancerFullOptions>();
            var newPropertyAddModels =
                new List<(CustomPropertyAddOrPutDto PropertyAddModel, List<Enhancement> Enhancements)>();
            foreach (var enhancement in enhancements)
            {
                var resource = resourceMap.GetValueOrDefault(enhancement.ResourceId);
                if (resource == null)
                {
                    continue;
                }

                var categoryOptions = categoryEnhancerOptionsMap.GetValueOrDefault(resource.CategoryId)
                    ?.GetValueOrDefault(enhancement.EnhancerId);

                var targetOptionsGroup = categoryOptions?.Options?.TargetOptions
                    ?.Where(x => x.Target == enhancement.Target).ToArray();

                if (targetOptionsGroup?.Any() != true)
                {
                    continue;
                }

                var targetDescriptor = _enhancerDescriptors[enhancement.EnhancerId][enhancement.Target];
                var targetOptions = targetOptionsGroup.FirstOrDefault(x => string.IsNullOrEmpty(x.DynamicTarget));
                if (!string.IsNullOrEmpty(enhancement.DynamicTarget))
                {
                    if (targetDescriptor.IsDynamic)
                    {
                        var dynamicTargetOptions =
                            targetOptionsGroup.FirstOrDefault(x => x.DynamicTarget == enhancement.DynamicTarget);
                        if (dynamicTargetOptions == null)
                        {
                            if (targetOptions?.AutoBindProperty == true)
                            {
                                dynamicTargetOptions = new EnhancerTargetFullOptions
                                {
                                    AutoBindProperty = true,
                                    DynamicTarget = enhancement.DynamicTarget,
                                    Target = enhancement.Target
                                };
                                categoryOptions!.Options!.TargetOptions!.Add(dynamicTargetOptions);
                                changedCategoryEnhancerOptions.Add(categoryOptions);
                            }
                        }

                        targetOptions = dynamicTargetOptions;
                    }
                    else
                    {
                        throw new DevException(
                            $"Found dynamic target {enhancement.DynamicTarget} for a static target: {targetDescriptor.Id}:{targetDescriptor.Name} in enhancer:{_enhancerDescriptors[enhancement.EnhancerId].Name}");
                    }
                }

                if (targetOptions == null)
                {
                    continue;
                }

                // match property
                if (targetOptions is {PropertyId: not null, PropertyPool: not null})
                {
                    switch (targetOptions.PropertyPool.Value)
                    {
                        case PropertyPool.Reserved:
                        {
                            if (!SpecificEnumUtils<Abstractions.Models.Domain.Constants.ReservedProperty>.Values
                                    .Contains(
                                        (Abstractions.Models.Domain.Constants.ReservedProperty) targetOptions
                                            .PropertyId))
                            {
                                throw new Exception(
                                    _enhancerLocalizer
                                        .Enhancer_Target_Options_PropertyIdIsNotFoundInReservedResourceProperties(
                                            targetOptions.PropertyId.Value));
                            }

                            break;
                        }
                        case PropertyPool.Custom:
                        {
                            if (!propertyMap.TryGetValue(targetOptions.PropertyId.Value, out var property))
                            {
                                throw new Exception(
                                    _enhancerLocalizer
                                        .Enhancer_Target_Options_PropertyIdIsNotFoundInCustomResourceProperties(
                                            targetOptions.PropertyId.Value));
                            }

                            break;
                        }
                        case PropertyPool.Internal:
                        case PropertyPool.All:
                        default:
                            throw new Exception(
                                _enhancerLocalizer.Enhancer_Target_Options_PropertyTypeIsNotSupported(targetOptions
                                    .PropertyPool.Value));
                    }
                }
                else
                {
                    if (targetOptions.PropertyId.HasValue || targetOptions.PropertyPool.HasValue)
                    {
                        var name = targetDescriptor.IsDynamic
                            ? enhancement.DynamicTarget ?? string.Empty
                            : targetDescriptor.Name;

                        if (!targetOptions.PropertyId.HasValue)
                        {
                            throw new Exception(
                                _enhancerLocalizer.Enhancer_Target_Options_PropertyIdIsNullButPropertyTypeIsNot(
                                    targetOptions.PropertyPool!.Value, name));
                        }

                        throw new Exception(
                            _enhancerLocalizer.Enhancer_Target_Options_PropertyTypeIsNullButPropertyIdIsNot(
                                targetOptions.PropertyId.Value, name));
                    }
                    else
                    {
                        if (targetOptions.AutoBindProperty == true)
                        {
                            if (targetDescriptor.ReservedPropertyCandidate.HasValue)
                            {
                                targetOptions.PropertyId =
                                    (int) targetDescriptor.ReservedPropertyCandidate.Value;
                                targetOptions.PropertyPool = PropertyPool.Reserved;
                            }
                            else
                            {
                                var name = targetDescriptor.IsDynamic
                                    ? enhancement.DynamicTarget
                                    : targetDescriptor.Name;
                                if (!string.IsNullOrEmpty(name))
                                {
                                    var propertyCandidate = propertyMap.Values.FirstOrDefault(p =>
                                        p.Type == targetDescriptor.PropertyType && p.Name == name);
                                    if (propertyCandidate == null)
                                    {
                                        var kv = newPropertyAddModels.FirstOrDefault(x =>
                                            x.PropertyAddModel.Name == name && x.PropertyAddModel.Type ==
                                            targetDescriptor.PropertyType);
                                        if (kv == default)
                                        {
                                            kv = (new CustomPropertyAddOrPutDto
                                            {
                                                Name = name,
                                                Type = targetDescriptor.PropertyType
                                            }, []);

                                            object? options = null;
                                            // todo: Standardize serialization of options
                                            switch (targetDescriptor.PropertyType)
                                            {
                                                case PropertyType.SingleChoice:
                                                {
                                                    options = new SingleChoicePropertyOptions()
                                                        {AllowAddingNewDataDynamically = true};
                                                    kv.PropertyAddModel.Options = JsonConvert.SerializeObject(options);
                                                    break;
                                                }
                                                case PropertyType.MultipleChoice:
                                                {
                                                    options = new MultipleChoicePropertyOptions()
                                                        {AllowAddingNewDataDynamically = true};
                                                    break;
                                                }
                                                case PropertyType.Multilevel:
                                                {
                                                    options = new MultilevelPropertyOptions
                                                        {AllowAddingNewDataDynamically = true};
                                                    break;
                                                }
                                            }

                                            if (options != null)
                                            {
                                                kv.PropertyAddModel.Options = JsonConvert.SerializeObject(options);
                                            }

                                            newPropertyAddModels.Add(kv);
                                        }

                                        kv.Enhancements.Add(enhancement);
                                    }
                                    else
                                    {
                                        targetOptions.PropertyId = propertyCandidate.Id;
                                        targetOptions.PropertyPool = PropertyPool.Custom;
                                    }

                                    changedCategoryEnhancerOptions.Add(categoryOptions!);
                                }
                            }
                        }
                        else
                        {
                            // no property bound, ignore
                            continue;
                        }
                    }
                }

                enhancementTargetOptionsMap[enhancement] = targetOptions;
            }

            if (newPropertyAddModels.Any())
            {
                var properties =
                    await _customPropertyService.AddRange(
                        newPropertyAddModels.Select(x => x.PropertyAddModel).ToArray());
                for (var i = 0; i < properties.Count; i++)
                {
                    var es = newPropertyAddModels[i].Enhancements;
                    var property = properties[i];
                    foreach (var e in es)
                    {
                        enhancementTargetOptionsMap[e].PropertyId = property.Id;
                        enhancementTargetOptionsMap[e].PropertyPool = PropertyPool.Custom;

                    }

                    propertyMap[property.Id] = property;
                }
            }

            if (changedCategoryEnhancerOptions.Any())
            {
                await _categoryEnhancerService.PutAll(changedCategoryEnhancerOptions.ToArray());
            }

            // var enhancementsIntegratedWithAlias = enhancementTargetOptionsMap
            //     .Where(v => v.Value.IntegrateWithAlias == true && v.Key.Value != null)
            //     .Select(v => v.Key).ToList();
            // if (enhancementsIntegratedWithAlias.Any())
            // {
            //     var replacedValueMap = await _standardValueService.IntegrateWithAlias(
            //         enhancementsIntegratedWithAlias.ToDictionary(d => d.Value!, d => d.ValueType));
            //     foreach (var e in enhancementsIntegratedWithAlias)
            //     {
            //         var replacedValue = replacedValueMap.GetValueOrDefault(e.Value!);
            //         if (replacedValue != null)
            //         {
            //             e.Value = replacedValue;
            //         }
            //     }
            // }

            var pvs = new List<CustomPropertyValue>();
            var addedPvKeys = new HashSet<string>();
            var enhancementCpvMap = enhancements.ToDictionary(d => d, d => (CustomPropertyValue?) null);
            var scopedRpvMap = new Dictionary<PropertyValueScope, Dictionary<int, ReservedPropertyValue>>();

            var changedProperties = new HashSet<CustomProperty>();

            foreach (var (enhancement, targetOptions) in enhancementTargetOptionsMap)
            {
                enhancement.PropertyPool = targetOptions.PropertyPool;
                enhancement.PropertyId = targetOptions.PropertyId;

                // use first not null enhancement for enhancements with same properties;
                var enhancerDescriptor = _enhancerDescriptors[enhancement.EnhancerId];
                var pvKey =
                    $"{enhancement.ResourceId}-{targetOptions.PropertyPool}-{targetOptions.PropertyId}-{enhancerDescriptor.PropertyValueScope}";

                if (addedPvKeys.Add(pvKey))
                {
                    Property? propertyDescriptor;

                    switch (targetOptions.PropertyPool!.Value)
                    {
                        case PropertyPool.Reserved:
                        {
                            propertyDescriptor =
                                PropertyInternals.BuiltinPropertyMap.GetValueOrDefault(
                                    (ResourceProperty) targetOptions.PropertyId!.Value);
                            break;
                        }
                        case PropertyPool.Custom:
                        {
                            propertyDescriptor = propertyMap.GetValueOrDefault(targetOptions.PropertyId!.Value)
                                ?.ToProperty();
                            break;
                        }
                        case PropertyPool.Internal:
                        case PropertyPool.All:
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (propertyDescriptor == null)
                    {
                        throw new Exception(
                            _propertyLocalizer.DescriptorIsNotFound(enhancement.PropertyPool!.Value,
                                enhancement.PropertyId!.Value));
                    }

                    var targetDescriptor = _enhancerDescriptors[enhancement.EnhancerId][enhancement.Target];
                    var value = targetDescriptor.EnhancementConverter == null
                        ? enhancement.Value
                        : targetDescriptor.EnhancementConverter.Convert(enhancement.Value, propertyDescriptor);

                    var nv = await _standardValueService.Convert(value, enhancement.ValueType,
                        propertyDescriptor.Type.GetBizValueType());

                    switch (targetOptions.PropertyPool!.Value)
                    {
                        case PropertyPool.Reserved:
                        {
                            var scope = (PropertyValueScope) enhancerDescriptor.PropertyValueScope;
                            var rpv = scopedRpvMap.GetOrAdd(scope, () => new Dictionary<int, ReservedPropertyValue>())
                                .GetOrAdd(enhancement.ResourceId, () => new ReservedPropertyValue
                                {
                                    ResourceId = enhancement.ResourceId,
                                    Scope = (int) scope,
                                });

                            switch ((Abstractions.Models.Domain.Constants.ReservedProperty) targetOptions.PropertyId!
                                        .Value)
                            {
                                case Abstractions.Models.Domain.Constants.ReservedProperty.Introduction:
                                    rpv.Introduction = nv as string;
                                    break;
                                case Abstractions.Models.Domain.Constants.ReservedProperty.Rating:
                                    rpv.Rating = nv is decimal nv1 ? nv1 : null;
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }

                            break;
                        }
                        case PropertyPool.Custom:
                        {
                            var property = propertyMap[targetOptions.PropertyId!.Value];
                            var result = await _customPropertyValueService.CreateTransient(nv,
                                propertyDescriptor.Type.GetBizValueType(),
                                property,
                                enhancement.ResourceId, enhancerDescriptor.PropertyValueScope);
                            if (result.HasValue)
                            {
                                var (pv, propertyChanged) = result.Value;
                                if (currentPropertyValuesMap.TryGetValue(pv.BizKey, out var cpv))
                                {
                                    pv.Id = cpv.Id;
                                }

                                pvs.Add(pv);
                                enhancementCpvMap[enhancement] = pv;
                                if (propertyChanged)
                                {
                                    changedProperties.Add(property);
                                }
                            }

                            break;
                        }
                        case PropertyPool.Internal:
                        case PropertyPool.All:
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            // delete not relative property values;
            // add new property values;
            // update current property values;
            // update record status

            if (changedProperties.Any())
            {
                await _customPropertyService.UpdateRange(changedProperties.Select(p => p.ToDbModel()!).ToArray());
            }

            var valuesToUpdate = pvs.Where(pv => pv.Id > 0).ToList();
            var valuesToAdd = pvs.Except(valuesToUpdate).ToList();
            await _customPropertyValueService.UpdateRange(valuesToUpdate);
            await _customPropertyValueService.AddRange(valuesToAdd);

            if (scopedRpvMap.Any())
            {
                var scopes = scopedRpvMap.Keys.Cast<int>().ToHashSet();
                var reservedValues = (await _reservedPropertyValueService.GetAll(x => scopes.Contains(x.Scope)))
                    .GroupBy(d => d.Scope).ToDictionary(d => (PropertyValueScope) d.Key,
                        d => d.GroupBy(x => x.ResourceId).ToDictionary(y => y.Key, y => y.First()));
                var reservedPropertyValuesToBeAdded = new List<ReservedPropertyValue>();
                var reservedPropertyValuesToBeUpdated = new List<ReservedPropertyValue>();
                foreach (var (scope, reservedResourceValues) in scopedRpvMap)
                {
                    foreach (var (rId, rv) in reservedResourceValues)
                    {
                        var dbValue = reservedValues.GetValueOrDefault(scope)?.GetValueOrDefault(rId);
                        if (dbValue != null)
                        {
                            rv.Id = dbValue.Id;
                            reservedPropertyValuesToBeUpdated.Add(rv);
                        }
                        else
                        {
                            reservedPropertyValuesToBeAdded.Add(rv);
                        }
                    }
                }

                await _reservedPropertyValueService.UpdateRange(reservedPropertyValuesToBeUpdated);
                await _reservedPropertyValueService.AddRange(reservedPropertyValuesToBeAdded);
            }

            var remainingPvScopeKeys = enhancements
                .Select(e => e.EnhancerId)
                .ToHashSet()
                .Select(x => _enhancerDescriptors.TryGet(x)?.PropertyValueScope ?? -1)
                .Where(x => x > 0)
                .ToDictionary(d => d, d => pvs.Where(pv => pv.Scope == d).Select(x => x.BizKey));

            // var remainingPvScopeKeys = pvs.GroupBy(d => d.Scope)
            //     .ToDictionary(d => d.Key, d => d.Select(p => p.BizKey).ToHashSet());
            var valueIdsToDelete = new List<int>();
            foreach (var scopeValues in currentPropertyValues.GroupBy(x => x.Scope))
            {
                if (remainingPvScopeKeys.TryGetValue(scopeValues.Key, out var bizKeys))
                {
                    valueIdsToDelete.AddRange(scopeValues.Where(x => !bizKeys.Contains(x.BizKey)).Select(v => v.Id));
                }
            }

            await _customPropertyValueService.RemoveByKeys(valueIdsToDelete);

            await _enhancementService.UpdateRange(enhancements);

            var resourceIdEnhancerIdsMap = enhancements.GroupBy(d => d.ResourceId).ToDictionary(d => d.Key,
                d => d.Select(x => x.EnhancerId).ToHashSet());
            var records = await _enhancementRecordService.GetAll(x => resourceIds.Contains(x.ResourceId));
            records.RemoveAll(r => !resourceIdEnhancerIdsMap[r.ResourceId].Contains(r.EnhancerId));
            foreach (var record in records)
            {
                record.Status = EnhancementRecordStatus.ContextApplied;
                record.ContextAppliedAt = DateTime.Now;
            }

            await _enhancementRecordService.Update(records);
        }

        protected async Task CreateEnhancementContext(List<Abstractions.Models.Domain.Resource> targetResources,
            HashSet<int>? restrictedEnhancerIds, bool apply, CancellationToken ct)
        {
            var categoryIds = targetResources.Select(c => c.CategoryId).ToHashSet();
            var categoryIdEnhancerOptionsMap =
                (await _categoryService.GetByKeys(categoryIds, CategoryAdditionalItem.EnhancerOptions)).ToDictionary(
                    d => d.Id, d => d.EnhancerOptions);
            var tasks =
                new Dictionary<IEnhancer,
                    Dictionary<EnhancerFullOptions, List<Bakabase.Abstractions.Models.Domain.Resource>>>();
            foreach (var tr in targetResources)
            {
                var enhancerOptions = categoryIdEnhancerOptionsMap.GetValueOrDefault(tr.CategoryId);
                if (enhancerOptions != null)
                {
                    foreach (var eo in enhancerOptions.Cast<CategoryEnhancerFullOptions>())
                    {
                        if (eo is {Active: true, Options: not null} && (restrictedEnhancerIds == null ||
                                                                        restrictedEnhancerIds.Contains(eo.EnhancerId)))
                        {
                            var enhancer = _enhancers.GetValueOrDefault(eo.EnhancerId);
                            if (enhancer != null)
                            {
                                if (!tasks.TryGetValue(enhancer, out var optionsAndResources))
                                {
                                    tasks[enhancer] = optionsAndResources = [];
                                }

                                if (!optionsAndResources.TryGetValue(eo.Options!, out var resources))
                                {
                                    optionsAndResources[eo.Options] = resources = [];
                                }

                                resources.Add(tr);
                            }
                        }
                    }
                }
            }

            var resourceIds = targetResources.Select(r => r.Id).ToHashSet();
            // ResourceId - EnhancerId - Record
            var resourceEnhancementRecordMap =
                (await _enhancementRecordService.GetAll(x => resourceIds.Contains(x.ResourceId)))
                .GroupBy(d => d.ResourceId)
                .ToDictionary(
                    d => d.Key,
                    d => d.GroupBy(c => c.EnhancerId).ToDictionary(e => e.Key, e => e.FirstOrDefault()));

            foreach (var (enhancer, optionsAndResources) in tasks)
            {
                foreach (var (options, resources) in optionsAndResources)
                {
                    foreach (var resource in resources)
                    {
                        var record = resourceEnhancementRecordMap.GetValueOrDefault(resource.Id)
                            ?.GetValueOrDefault(enhancer.Id);

                        List<Enhancement>? enhancements = null;

                        if (record == null)
                        {
                            var enhancementRawValues = await enhancer.CreateEnhancements(resource, options, ct);
                            if (enhancementRawValues?.Any() == true)
                            {
                                enhancements = enhancementRawValues.Select(v =>
                                    new Enhancement
                                    {
                                        Target = v.Target,
                                        EnhancerId = enhancer.Id,
                                        ResourceId = resource.Id,
                                        Value = v.Value,
                                        ValueType = v.ValueType,
                                        DynamicTarget = v.DynamicTarget
                                    }).ToList();

                                await _enhancementService.AddRange(enhancements);
                            }
                            else
                            {
                                enhancements = [];
                            }

                            record = new EnhancementRecord
                            {
                                EnhancerId = enhancer.Id,
                                ResourceId = resource.Id,
                                ContextCreatedAt = DateTime.Now,
                                Status = EnhancementRecordStatus.ContextCreated
                            };

                            await _enhancementRecordService.Add(record);
                        }

                        if (apply)
                        {
                            // null if record is not null before.
                            enhancements ??= await _enhancementService.GetAll(x =>
                                x.ResourceId == resource.Id && x.EnhancerId == enhancer.Id);
                            await ApplyEnhancementsToResources(enhancements, ct);
                        }
                    }
                }
            }
        }

        public async Task EnhanceResource(int resourceId, HashSet<int>? enhancerIds, CancellationToken ct)
        {
            var resource = (await _resourceService.Get(resourceId, ResourceAdditionalItem.None))!;
            await CreateEnhancementContext([resource], enhancerIds, true, ct);
        }

        public async Task EnhanceAll(CancellationToken ct)
        {
            var resources = await _resourceService.GetAll(null, ResourceAdditionalItem.None);
            await CreateEnhancementContext(resources, null, true, ct);
        }

        public async Task ReapplyEnhancementsByCategory(int categoryId, int enhancerId, CancellationToken ct)
        {
            var resources = await _resourceService.GetAll(x => x.CategoryId == categoryId);
            var resourceIds = resources.Select(r => r.Id).ToList();
            var enhancements =
                await _enhancementService.GetAll(x => resourceIds.Contains(x.ResourceId) && x.EnhancerId == enhancerId);
            await ApplyEnhancementsToResources(enhancements, ct);
        }

        public async Task ReapplyEnhancementsByResources(int[] resourceIds, int[] enhancerIds, CancellationToken ct)
        {
            var enhancements = await _enhancementService.GetAll(x =>
                resourceIds.Contains(x.ResourceId) && enhancerIds.Contains(x.EnhancerId));
            await ApplyEnhancementsToResources(enhancements, ct);
        }
    }
}