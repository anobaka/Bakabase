using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using DotNext.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using IEnhancer = Bakabase.Modules.Enhancer.Abstractions.Components.IEnhancer;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Bakabase.Abstractions.Components.Tracing;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Enhancer.Services
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
        private readonly IEnhancerDescriptors _enhancerDescriptors;
        private readonly IEnhancerLocalizer _enhancerLocalizer;
        private readonly IPropertyLocalizer _propertyLocalizer;
        private readonly Dictionary<int, IEnhancer> _enhancers;
        private readonly ILogger<EnhancerService> _logger;
        private readonly ICategoryService _categoryService;
        private readonly IEnhancementRecordService _enhancementRecordService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IPropertyService _propertyService;
        private readonly IMediaLibraryV2Service _mediaLibraryV2Service;
        private readonly BakaTracingContext _tracingContext;

        public EnhancerService(ICustomPropertyService customPropertyService, IResourceService resourceService,
            ICustomPropertyValueService customPropertyValueService,
            IEnhancementService enhancementService, ICategoryEnhancerOptionsService categoryEnhancerService,
            IStandardValueService standardValueService, IEnhancerLocalizer enhancerLocalizer,
            IEnhancerDescriptors enhancerDescriptors, IEnumerable<IEnhancer> enhancers, ILogger<EnhancerService> logger,
            ICategoryService categoryService, IEnhancementRecordService enhancementRecordService, IReservedPropertyValueService reservedPropertyValueService,
            IPropertyLocalizer propertyLocalizer, IServiceProvider serviceProvider, IPropertyService propertyService, IMediaLibraryV2Service mediaLibraryV2Service, BakaTracingContext tracingContext)
        {
            _customPropertyService = customPropertyService;
            _resourceService = resourceService;
            _customPropertyValueService = customPropertyValueService;
            _enhancementService = enhancementService;
            _categoryEnhancerService = categoryEnhancerService;
            _standardValueService = standardValueService;
            _enhancerLocalizer = enhancerLocalizer;
            _enhancerDescriptors = enhancerDescriptors;
            _logger = logger;
            _categoryService = categoryService;
            _enhancementRecordService = enhancementRecordService;
            _reservedPropertyValueService = reservedPropertyValueService;
            _propertyLocalizer = propertyLocalizer;
            _serviceProvider = serviceProvider;
            _propertyService = propertyService;
            _mediaLibraryV2Service = mediaLibraryV2Service;
            _tracingContext = tracingContext;
            _enhancers = enhancers.ToDictionary(d => d.Id, d => d);
        }

        protected async Task<Dictionary<Enhancement, EnhancerTargetFullOptions>> PrepareEnhancementOptions(
            List<Enhancement> enhancements, Dictionary<int, Resource> resourceMap,
            Dictionary<int, CustomProperty> propertyMap)
        {
            var categoryIds = resourceMap.Values.Where(r => r.CategoryId > 0).Select(v => v.CategoryId).ToHashSet();
            var enhancerIds = enhancements.Select(s => s.EnhancerId).ToHashSet();
            var enhancerOptions = await _categoryEnhancerService.GetAll(x =>
                enhancerIds.Contains(x.EnhancerId) && categoryIds.Contains(x.CategoryId));

            var mediaLibraryV2Ids =
                resourceMap.Values.Where(r => r.CategoryId == 0).Select(r => r.MediaLibraryId).ToHashSet();
            var mediaLibraryV2List =
                await _mediaLibraryV2Service.GetByKeys(mediaLibraryV2Ids.ToArray(),
                    MediaLibraryV2AdditionalItem.Template);
            var mediaLibraryV2IdEnhancerOptionsMap = mediaLibraryV2List.Where(d => d.Template != null)
                .ToDictionary(d => d.Id, d => d.Template!.Enhancers?.ToDictionary(x => x.EnhancerId, x =>
                    new EnhancerFullOptions
                    {
                        TargetOptions = x.TargetOptions?.Select(t => new EnhancerTargetFullOptions
                        {
                            CoverSelectOrder = t.CoverSelectOrder,
                            PropertyId = t.PropertyId,
                            PropertyPool = t.PropertyPool,
                            DynamicTarget = t.DynamicTarget,
                            Target = t.Target,
                        }).ToList(),
                        Expressions = x.Expressions,
                        Requirements = x.Requirements?.Select(r => (EnhancerId)r).ToList(),
                        KeywordProperty = x.KeywordProperty
                    }));
            // CategoryId - EnhancerId - Options
            var categoryEnhancerOptionsMap = enhancerOptions.GroupBy(d => d.CategoryId)
                .ToDictionary(d => d.Key, d => d.ToDictionary(c => c.EnhancerId, c => c));
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

                var efo = resource.IsMediaLibraryV2
                    ? mediaLibraryV2IdEnhancerOptionsMap.GetValueOrDefault(resource.MediaLibraryId)
                        ?.GetValueOrDefault(enhancement.EnhancerId)
                    : categoryEnhancerOptionsMap.GetValueOrDefault(resource.CategoryId)
                        ?.GetValueOrDefault(enhancement.EnhancerId)?.Options;

                var categoryOptions = categoryEnhancerOptionsMap.GetValueOrDefault(resource.CategoryId)
                    ?.GetValueOrDefault(enhancement.EnhancerId);

                var targetOptionsGroup = efo?.TargetOptions
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
                            // Options for media library template will never enter this branch
                            if (targetOptions?.AutoBindProperty == true)
                            {
                                dynamicTargetOptions = new EnhancerTargetFullOptions
                                {
                                    AutoBindProperty = true,
                                    DynamicTarget = enhancement.DynamicTarget,
                                    Target = enhancement.Target
                                };
                                efo.TargetOptions!.Add(dynamicTargetOptions);
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
                if (targetOptions is { PropertyId: not null, PropertyPool: not null })
                {
                    switch (targetOptions.PropertyPool.Value)
                    {
                        case PropertyPool.Reserved:
                        {
                            if (!SpecificEnumUtils<ReservedProperty>.Values
                                    .Contains(
                                        (ReservedProperty)targetOptions
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
                    // if (targetOptions.PropertyId.HasValue || targetOptions.PropertyPool.HasValue)
                    // {
                    //     var name = targetDescriptor.IsDynamic
                    //         ? enhancement.DynamicTarget ?? string.Empty
                    //         : targetDescriptor.Name;
                    //
                    //     if (!targetOptions.PropertyId.HasValue)
                    //     {
                    //         throw new Exception(
                    //             _enhancerLocalizer.Enhancer_Target_Options_PropertyIdIsNullButPropertyTypeIsNot(
                    //                 targetOptions.PropertyPool!.Value, name));
                    //     }
                    //
                    //     throw new Exception(
                    //         _enhancerLocalizer.Enhancer_Target_Options_PropertyTypeIsNullButPropertyIdIsNot(
                    //             targetOptions.PropertyId.Value, name));
                    // }
                    // else
                    // {
                    // Options for media library template will never enter this branch
                    if (targetOptions.AutoBindProperty == true)
                    {
                        if (targetDescriptor.ReservedPropertyCandidate.HasValue)
                        {
                            targetOptions.PropertyId =
                                (int)targetDescriptor.ReservedPropertyCandidate.Value;
                            targetOptions.PropertyPool = PropertyPool.Reserved;
                            changedCategoryEnhancerOptions.Add(categoryOptions!);
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
                                                options = new SingleChoicePropertyOptions();
                                                // {AllowAddingNewDataDynamically = true};
                                                kv.PropertyAddModel.Options = JsonConvert.SerializeObject(options);
                                                break;
                                            }
                                            case PropertyType.MultipleChoice:
                                            {
                                                options = new MultipleChoicePropertyOptions();
                                                // {AllowAddingNewDataDynamically = true};
                                                break;
                                            }
                                            case PropertyType.Multilevel:
                                            {
                                                options = new MultilevelPropertyOptions();
                                                // {AllowAddingNewDataDynamically = true};
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
                    // }
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

            return enhancementTargetOptionsMap;
        }

        public async Task ApplyEnhancementsToResources(Dictionary<int, HashSet<int>> resourceIdEnhancerIdsMap,
            List<Enhancement> enhancements, CancellationToken ct)
        {
            var resourceIds = resourceIdEnhancerIdsMap.Keys.ToHashSet();
            var resourceMap = (await _resourceService.GetByKeys(resourceIds.ToArray())).ToDictionary(d => d.Id, d => d);

            var propertyMap = (await _customPropertyService.GetAll()).ToDictionary(d => d.Id, d => d);
            var currentPropertyValues =
                await _customPropertyValueService.GetAll(x => resourceIdEnhancerIdsMap.ContainsKey(x.ResourceId),
                    CustomPropertyValueAdditionalItem.None, true);
            var currentPropertyValuesMap = currentPropertyValues.ToDictionary(d => d.BizKey, d => d);

            var enhancementTargetOptionsMap = await PrepareEnhancementOptions(enhancements, resourceMap, propertyMap);

            var pvs = new List<CustomPropertyValue>();
            var addedPvKeys = new HashSet<string>();
            var scopedRpvMap = new Dictionary<PropertyValueScope, Dictionary<int, ReservedPropertyValue>>();

            var changedProperties = new HashSet<CustomProperty>();

            foreach (var (enhancement, targetOptions) in enhancementTargetOptionsMap)
            {
                ct.ThrowIfCancellationRequested();
                enhancement.PropertyPool = targetOptions.PropertyPool;
                enhancement.PropertyId = targetOptions.PropertyId;

                // use first not null enhancement for enhancements with same properties;
                var enhancerDescriptor = _enhancerDescriptors[enhancement.EnhancerId];
                var pvKey =
                    $"{enhancement.ResourceId}-{targetOptions.PropertyPool}-{targetOptions.PropertyId}-{enhancerDescriptor.PropertyValueScope}";

                if (addedPvKeys.Add(pvKey))
                {
                    Bakabase.Abstractions.Models.Domain.Property? propertyDescriptor;

                    switch (targetOptions.PropertyPool!.Value)
                    {
                        case PropertyPool.Reserved:
                        {
                            propertyDescriptor =
                                PropertyInternals.BuiltinPropertyMap.GetValueOrDefault(
                                    (ResourceProperty)targetOptions.PropertyId!.Value);
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
                            var scope = (PropertyValueScope)enhancerDescriptor.PropertyValueScope;
                            var rpv = scopedRpvMap.GetOrAdd(scope, _ => new Dictionary<int, ReservedPropertyValue>())
                                .GetOrAdd(enhancement.ResourceId, _ => new ReservedPropertyValue
                                {
                                    ResourceId = enhancement.ResourceId,
                                    Scope = (int)scope,
                                });

                            switch ((ReservedProperty)targetOptions.PropertyId!
                                        .Value)
                            {
                                case ReservedProperty.Introduction:
                                {
                                    rpv.Introduction = nv as string;
                                    break;
                                }
                                case ReservedProperty.Rating:
                                {
                                    rpv.Rating = nv is decimal nv1 ? nv1 : null;
                                    break;
                                }
                                case ReservedProperty.Cover:
                                {
                                    rpv.CoverPaths = nv as List<string>;
                                    break;
                                }
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
                    .GroupBy(d => d.Scope).ToDictionary(d => (PropertyValueScope)d.Key,
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

            var valueIdsToDelete = new List<int>();
            // resource id - scope - biz keys
            var currentApplyingPvScopedKeysByResources = resourceIdEnhancerIdsMap.ToDictionary(d => d.Key, d => d.Value
                .Select(x => _enhancerDescriptors.TryGet(x)?.PropertyValueScope ?? -1)
                .Where(x => x > 0)
                .ToDictionary(x => x, x => pvs.Where(pv => pv.Scope == x).Select(a => a.BizKey).ToHashSet()));

            var currentScopedPvsByResources =
                currentPropertyValues.GroupBy(v => v.ResourceId)
                    .ToDictionary(d => d.Key, d => d.GroupBy(a => a.Scope).ToDictionary(b => b.Key, b => b.ToArray()));

            foreach (var (rId, scopedPvBizKeys) in currentApplyingPvScopedKeysByResources)
            {
                if (currentScopedPvsByResources.TryGetValue(rId, out var scopedPvs))
                {
                    foreach (var (scope, pvBizKeys) in scopedPvBizKeys)
                    {
                        if (scopedPvs.TryGetValue(scope, out var currentPvs))
                        {
                            var gonePvs = currentPvs.Where(x => !pvBizKeys.Contains(x.BizKey));
                            valueIdsToDelete.AddRange(gonePvs.Select(a => a.Id));
                        }
                    }
                }
                
            }

            await _customPropertyValueService.RemoveByKeys(valueIdsToDelete);

            await _enhancementService.UpdateRange(enhancements);

            
            var records =
                await _enhancementRecordService.GetAll(x => resourceIds.Contains(x.ResourceId));
            records.RemoveAll(r => !resourceIdEnhancerIdsMap[r.ResourceId].Contains(r.EnhancerId));
            foreach (var record in records)
            {
                record.Status = EnhancementRecordStatus.ContextApplied;
                record.ContextAppliedAt = DateTime.Now;
            }

            await _enhancementRecordService.Update(records);
        }

        protected record ContextCreationTask(
            IEnhancer Enhancer,
            EnhancerFullOptions Options,
            Resource Resource,
            EnhancementRecord? Record,
            HashSet<ContextCreationTask> Dependencies,
            HashSet<ContextCreationTask> Dependents);

        protected async
            Task<List<ContextCreationTask>>
            BuildContextCreationTasks(List<Resource> targetResources, HashSet<int>? restrictedEnhancerIds)
        {
            var categoryIds = targetResources.Where(r => r.CategoryId > 0).Select(c => c.CategoryId).ToHashSet();
            var categoryIdEnhancerOptionsMap =
                (await _categoryService.GetByKeys(categoryIds, CategoryAdditionalItem.EnhancerOptions)).ToDictionary(
                    d => d.Id, d => d.EnhancerOptions);
            var mediaLibraryV2Ids =
                targetResources.Where(r => r.CategoryId == 0).Select(r => r.MediaLibraryId).ToHashSet();
            var mediaLibraryV2List =
                await _mediaLibraryV2Service.GetByKeys(mediaLibraryV2Ids.ToArray(),
                    MediaLibraryV2AdditionalItem.Template);
            var mediaLibraryV2IdEnhancerOptionsMap = mediaLibraryV2List.Where(d => d.Template != null)
                .ToDictionary(d => d.Id, d => d.Template!.Enhancers?.ToDictionary(x => x.EnhancerId, x =>
                    new EnhancerFullOptions
                    {
                        TargetOptions = x.TargetOptions?.Select(t => new EnhancerTargetFullOptions
                        {
                            CoverSelectOrder = t.CoverSelectOrder,
                            PropertyId = t.PropertyId,
                            PropertyPool = t.PropertyPool,
                            DynamicTarget = t.DynamicTarget,
                            Target = t.Target,
                        }).ToList(),
                        Expressions = x.Expressions,
                        Requirements = x.Requirements?.Select(r => (EnhancerId)r).ToList(),
                        KeywordProperty = x.KeywordProperty
                    }));

            var prevTasks =
                new Dictionary<IEnhancer,
                    Dictionary<EnhancerFullOptions, List<(Resource Resource, EnhancementRecord? Record)>>>();

            var resourceIds = targetResources.Select(r => r.Id).ToHashSet();
            // ResourceId - EnhancerId - Record
            var resourceEnhancementRecordMap =
                (await _enhancementRecordService.GetAll(x => resourceIds.Contains(x.ResourceId)))
                .GroupBy(d => d.ResourceId)
                .ToDictionary(
                    d => d.Key,
                    d => d.GroupBy(c => c.EnhancerId).ToDictionary(e => e.Key, e => e.FirstOrDefault()));

            foreach (var tr in targetResources)
            {
                if (tr.CategoryId > 0)
                {
                    var enhancerOptions = categoryIdEnhancerOptionsMap.GetValueOrDefault(tr.CategoryId);
                    if (enhancerOptions != null)
                    {
                        foreach (var eo in enhancerOptions.Cast<CategoryEnhancerFullOptions>())
                        {
                            if (eo is { Active: true, Options: not null } && (restrictedEnhancerIds == null ||
                                                                              restrictedEnhancerIds
                                                                                  .Contains(eo.EnhancerId)))
                            {
                                var enhancer = _enhancers.GetValueOrDefault(eo.EnhancerId);
                                if (enhancer != null)
                                {
                                    var record = resourceEnhancementRecordMap.GetValueOrDefault(tr.Id)
                                        ?.GetValueOrDefault(eo.EnhancerId);
                                    if (record?.Status != EnhancementRecordStatus.ContextApplied)
                                    {
                                        if (!prevTasks.TryGetValue(enhancer, out var optionsAndResources))
                                        {
                                            prevTasks[enhancer] = optionsAndResources = [];
                                        }

                                        if (!optionsAndResources.TryGetValue(eo.Options!, out var resources))
                                        {
                                            optionsAndResources[eo.Options] = resources = [];
                                        }

                                        resources.Add((tr, record));
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    var eosMap = mediaLibraryV2IdEnhancerOptionsMap.GetValueOrDefault(tr.MediaLibraryId);
                    if (eosMap != null)
                    {
                        foreach (var (eId, eos) in eosMap)
                        {
                            var enhancer = _enhancers.GetValueOrDefault(eId);
                            if (enhancer != null)
                            {
                                var record = resourceEnhancementRecordMap.GetValueOrDefault(tr.Id)
                                    ?.GetValueOrDefault(eId);
                                if (record?.Status != EnhancementRecordStatus.ContextApplied)
                                {
                                    if (!prevTasks.TryGetValue(enhancer, out var optionsAndResources))
                                    {
                                        prevTasks[enhancer] = optionsAndResources = [];
                                    }

                                    if (!optionsAndResources.TryGetValue(eos, out var resources))
                                    {
                                        optionsAndResources[eos] = resources = [];
                                    }

                                    resources.Add((tr, record));
                                }
                            }
                        }
                    }
                }
            }

            var tasks = new List<ContextCreationTask>();
            foreach (var (enhancer, optionsAndResources) in prevTasks)
            {
                foreach (var (options, resources) in optionsAndResources)
                {
                    foreach (var rr in resources)
                    {
                        tasks.Add(new ContextCreationTask(enhancer, options, rr.Resource, rr.Record, [], []));
                    }
                }
            }

            foreach (var r in targetResources)
            {
                var groupTasks = tasks.Where(t => t.Resource.Id == r.Id).ToList();
                var enhancerTaskMap = groupTasks.ToDictionary(t => t.Enhancer.Id, t => t);
                foreach (var t in groupTasks)
                {
                    if (t.Options.Requirements != null)
                    {
                        foreach (var req in t.Options.Requirements)
                        {
                            if (enhancerTaskMap.TryGetValue((int)req, out var dependencyTask))
                            {
                                t.Dependencies.Add(dependencyTask);
                                dependencyTask.Dependents.Add(t);
                            }
                        }
                    }
                }

                // Detect dependency cycles within this resource's tasks
                var visited = new HashSet<ContextCreationTask>();
                var stack = new Stack<ContextCreationTask>();

                foreach (var node in groupTasks.Where(node => !visited.Contains(node)))
                {
                    Dfs(node);
                }

                continue;

                void Dfs(ContextCreationTask node)
                {
                    if (stack.Contains(node))
                    {
                        var cycleNodes = new List<ContextCreationTask>();
                        foreach (var s in stack)
                        {
                            cycleNodes.Add(s);
                            if (s == node) break;
                        }

                        cycleNodes.Reverse();
                        cycleNodes.Add(node);
                        var cycleIds = string.Join("->", cycleNodes.Select(x => x.Enhancer.Id));
                        throw new DevException(
                            $"Dependency cycle detected among enhancers for resource {r.Id}: {cycleIds}");
                    }

                    if (!visited.Add(node))
                    {
                        return;
                    }

                    stack.Push(node);
                    foreach (var dep in node.Dependencies)
                    {
                        Dfs(dep);
                    }

                    stack.Pop();
                }
            }

            return tasks;
        }

        private async Task CreateEnhancementContext(List<Resource> targetResources,
            HashSet<int>? restrictedEnhancerIds, bool apply, Func<int, Task>? onProgress,
            Func<string, Task>? onProcessChange, PauseToken pt,
            CancellationToken ct)
        {
            var enhanceTasks = await BuildContextCreationTasks(targetResources, restrictedEnhancerIds);
            var taskCount = enhanceTasks.Count;
            var doneCount = 0;
            var currentPercentage = 0;

            // Producer-Consumer pipeline: producers compute, single consumer persists/applies sequentially
            using var applyEnhancementsQueue =
                new BlockingCollection<(IEnhancer Enhancer, Resource Resource, List<Enhancement>? Enhancements,
                    EnhancementRecord? OriginalRecord)>(1);

            var applyEnhancementsQueueRef = applyEnhancementsQueue;
            var consumerTaskErrorBox = new StrongBox<bool>();

            // Start consumer first
            var consumerTask = Task.Run(async () =>
            {
                try
                {
                    await using var scope = _serviceProvider.CreateAsyncScope();
                    var sp = scope.ServiceProvider;
                    var enhancementService = sp.GetRequiredService<IEnhancementService>();
                    var enhancementRecordService = sp.GetRequiredService<IEnhancementRecordService>();
                    var enhancerService = sp.GetRequiredService<IEnhancerService>();
                    foreach (var item in applyEnhancementsQueueRef.GetConsumingEnumerable())
                    {
                        await pt.WaitWhilePausedAsync(ct);

                        var enhancer = item.Enhancer;
                        var resource = item.Resource;
                        var record = item.OriginalRecord;
                        var enhancements = item.Enhancements;

                        if (record == null)
                        {
                            enhancements ??= [];
                            if (enhancements.Any())
                            {
                                await enhancementService.AddRange(enhancements);
                            }

                            record = new EnhancementRecord
                            {
                                EnhancerId = enhancer.Id,
                                ResourceId = resource.Id,
                                ContextCreatedAt = DateTime.Now,
                                Status = EnhancementRecordStatus.ContextCreated
                            };
                            await enhancementRecordService.Add(record);
                        }

                        if (apply)
                        {
                            enhancements ??= await enhancementService.GetAll(x =>
                                x.ResourceId == resource.Id && x.EnhancerId == enhancer.Id);
                            _logger.LogInformation(
                                $"Applying {enhancements.Count} enhancements generated by [{enhancer.Id}:{(EnhancerId)enhancer.Id}] to resource [{resource.Id}:{resource.Path}]");
                            await enhancerService.ApplyEnhancementsToResources(
                                new Dictionary<int, HashSet<int>> { { resource.Id, [enhancer.Id] } }, enhancements, ct);
                        }

                        doneCount++;
                        var newPercentage = (int)Math.Floor(doneCount * 100.0 / taskCount);
                        if (newPercentage != currentPercentage)
                        {
                            currentPercentage = newPercentage;
                            if (onProgress != null)
                            {
                                await onProgress(currentPercentage);
                            }
                        }

                        if (onProcessChange != null)
                        {
                            await onProcessChange($"{doneCount}/{taskCount}");
                        }
                    }
                }
                catch (Exception)
                {
                    consumerTaskErrorBox.Value = true;
                    throw;
                }
            }, ct);

            var producerTasks = new List<Task>();
            var enhancerTasksGroups = enhanceTasks.GroupBy(t => t.Enhancer.Id);
            foreach (var tasks in enhancerTasksGroups)
            {
                var enhanceTask = Task.Run(async () =>
                {
                    var set = tasks.ToHashSet();
                    while (set.Any() && !consumerTaskErrorBox.Value)
                    {
                        ct.ThrowIfCancellationRequested();
                        await pt.WaitWhilePausedAsync(ct);
                        var nextTasks = set.Where(t => t.Dependencies.IsNullOrEmpty()).ToArray();
                        if (!nextTasks.Any())
                        {
                            _logger.LogInformation($"[{tasks.Key}:{(EnhancerId)tasks.Key}] has {set.Count} tasks are waiting for dependencies to be resolved");
                            await Task.Delay(1000, ct);
                        }
                        else
                        {
                            foreach (var task in nextTasks)
                            {
                                var resource =
                                    (await _resourceService.Get(task.Resource.Id, ResourceAdditionalItem.All))!;
                                List<Enhancement>? enhancements = null;
                                if (task.Record == null)
                                {
                                    _logger.LogInformation(
                                        $"Generate enhancement raw values by [{task.Enhancer.Id}:{(EnhancerId)task.Enhancer.Id}] for resource [{resource.Id}:{resource.Path}]");
                                    await pt.WaitWhilePausedAsync(ct);
                                    var enhancementRawValues =
                                        await task.Enhancer.CreateEnhancements(resource, task.Options,
                                            ct);
                                    enhancements = enhancementRawValues?.Any() == true
                                        ? enhancementRawValues.Select(v => new Enhancement
                                        {
                                            Target = v.Target,
                                            EnhancerId = task.Enhancer.Id,
                                            ResourceId = task.Resource.Id,
                                            Value = v.Value,
                                            ValueType = v.ValueType,
                                            DynamicTarget = v.DynamicTarget
                                        }).ToList()!
                                        : [];
                                }

                                applyEnhancementsQueueRef.Add((task.Enhancer, resource, enhancements, task.Record),
                                    ct);

                                foreach (var dep in task.Dependents)
                                {
                                    dep.Dependencies?.Remove(task);
                                }

                                var x = set.Remove(task);
                            }
                        }
                    }
                }, ct);
                producerTasks.Add(enhanceTask);
            }

            try
            {
                await Task.WhenAll(producerTasks);
            }
            finally
            {
                applyEnhancementsQueue.CompleteAdding();
                await consumerTask;
            }

            if (onProgress != null)
            {
                await onProgress(100);
            }
        }

        public async Task EnhanceResource(int resourceId, HashSet<int>? enhancerIds, PauseToken pt,
            CancellationToken ct)
        {
            var resource = (await _resourceService.Get(resourceId, ResourceAdditionalItem.None))!;
            await CreateEnhancementContext([resource], enhancerIds, true, null, null, pt, ct);
        }

        public async Task EnhanceAll(Func<int, Task>? onProgress, Func<string, Task>? onProcessChange, PauseToken pt,
            CancellationToken ct)
        {
            var resources = await _resourceService.GetAll(null, ResourceAdditionalItem.None);
            await CreateEnhancementContext(resources, null, true, onProgress, onProcessChange, pt, ct);
        }

        public async Task ReapplyEnhancementsByCategory(int categoryId, int enhancerId, CancellationToken ct)
        {
            var resources = await _resourceService.GetAll(x => x.CategoryId == categoryId);
            var resourceIds = resources.Select(r => r.Id).ToList();
            var enhancements =
                await _enhancementService.GetAll(x => resourceIds.Contains(x.ResourceId) && x.EnhancerId == enhancerId);
            var resourceIdEnhancerIdsMap = resourceIds.ToDictionary(d => d, d => new HashSet<int> { enhancerId });
            await ApplyEnhancementsToResources(resourceIdEnhancerIdsMap, enhancements, ct);
        }

        public async Task ReapplyEnhancementsByResources(int[] resourceIds, int[] enhancerIds, CancellationToken ct)
        {
            var enhancements = await _enhancementService.GetAll(x =>
                resourceIds.Contains(x.ResourceId) && enhancerIds.Contains(x.EnhancerId));
            var enhancerIdsSet = enhancerIds.ToHashSet();
            var resourceIdEnhancerIdsMap = resourceIds.ToDictionary(d => d, d => enhancerIdsSet);
            await ApplyEnhancementsToResources(resourceIdEnhancerIdsMap, enhancements, ct);
        }

        public async Task ReapplyEnhancementsByResources(Dictionary<int, int[]> resourceIdsEnhancerIdsMap,
            CancellationToken ct)
        {
            var resourceIds = resourceIdsEnhancerIdsMap.Keys.ToList();
            var enhancements = await _enhancementService.GetAll(x => resourceIds.Contains(x.ResourceId));

            enhancements.RemoveAll(x => !resourceIdsEnhancerIdsMap[x.ResourceId].Contains(x.EnhancerId));
            var resourceIdEnhancerIdsMap = resourceIdsEnhancerIdsMap.ToDictionary(d => d.Key, d => d.Value.ToHashSet());
            await ApplyEnhancementsToResources(resourceIdEnhancerIdsMap, enhancements, ct);
        }

        public async Task Enhance(Resource resource, Dictionary<int, EnhancerFullOptions> optionsMap)
        {
            var propertyMap = (await _propertyService.GetProperties(PropertyPool.All)).ToMap();
            foreach (var (enhancerId, fullOptions) in optionsMap)
            {
                var enhancer = _enhancers.GetValueOrDefault(enhancerId);
                if (enhancer != null)
                {
                    var rawValues = await enhancer.CreateEnhancements(resource, fullOptions, CancellationToken.None);
                    if (rawValues != null)
                    {
                        var enhancerDescriptor = _enhancerDescriptors[enhancerId];
                        foreach (var rawValue in rawValues)
                        {
                            var targetDescriptor = enhancerDescriptor.Targets.First(x => x.Id == rawValue.Target);
                            var targetOptions = fullOptions.TargetOptions?.FirstOrDefault(x =>
                                x.Target == rawValue.Target && rawValue.DynamicTarget == x.DynamicTarget);
                            if (targetDescriptor.IsDynamic && rawValue.DynamicTarget.IsNotEmpty() &&
                                targetOptions == null)
                            {
                                targetOptions = fullOptions.TargetOptions?.FirstOrDefault(x =>
                                    x.Target == rawValue.Target && x.DynamicTarget.IsNullOrEmpty());
                            }

                            if (targetOptions != null)
                            {
                                Bakabase.Abstractions.Models.Domain.Property? property = null;
                                if (targetOptions.AutoBindProperty == true)
                                {
                                    if (targetDescriptor.ReservedPropertyCandidate.HasValue)
                                    {
                                        property = propertyMap.GetValueOrDefault(PropertyPool.Reserved)
                                            ?.GetValueOrDefault((int)targetDescriptor.ReservedPropertyCandidate);
                                    }
                                    else
                                    {
                                        var pd = PropertyInternals.DescriptorMap[targetDescriptor.PropertyType];
                                        property = new Bakabase.Abstractions.Models.Domain.Property(PropertyPool.Custom,
                                            0, targetDescriptor.PropertyType,
                                            targetDescriptor.Name, pd.InitializeOptions());
                                    }
                                }
                                else
                                {
                                    if (targetOptions.PropertyPool.HasValue && targetOptions.PropertyId.HasValue)
                                    {
                                        property = propertyMap.GetValueOrDefault(targetOptions.PropertyPool.Value)
                                            ?.GetValueOrDefault(targetOptions.PropertyId.Value);
                                    }
                                }

                                if (property != null)
                                {
                                    var value = targetDescriptor.EnhancementConverter == null
                                        ? rawValue.Value
                                        : targetDescriptor.EnhancementConverter.Convert(rawValue.Value, property);

                                    var nv = await _standardValueService.Convert(value, rawValue.ValueType,
                                        property.Type.GetBizValueType());

                                    resource.Properties ??= [];
                                    var rpp = resource.Properties.GetOrAdd((int)property.Pool, _ => [])!;
                                    Resource.Property rp;
                                    if (property.Id > 0)
                                    {
                                        rp = rpp.GetOrAdd(
                                            property.Id,
                                            _ => new Resource.Property(property.Name,
                                                property.Type, property.Type.GetDbValueType(),
                                                property.Type.GetBizValueType(), [], true, property.Order));
                                    }
                                    else
                                    {
                                        var t = rpp.Values.FirstOrDefault(x =>
                                            x.Type == property.Type && x.Name == property.Name);
                                        if (t == null)
                                        {
                                            // Fake property
                                            var fakeId = Math.Max(rpp.Keys.Any() ? rpp.Keys.Max() : 0, 9999) + 1;
                                            t = new Resource.Property(property.Name,
                                                property.Type, property.Type.GetDbValueType(),
                                                property.Type.GetBizValueType(), [], true, property.Order);
                                            rpp[fakeId] = t;
                                        }

                                        rp = t;
                                    }

                                    rp.Values ??= [];
                                    rp.Values.Add(new Resource.Property.PropertyValue(
                                        enhancerDescriptor.PropertyValueScope, null, nv, nv));
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}