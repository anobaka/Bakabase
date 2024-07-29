using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Helpers;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Components;
using Bakabase.Modules.Enhancer.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bootstrap.Components.Logging.LogService.Services;
using IEnhancer = Bakabase.Abstractions.Components.Enhancer.IEnhancer;

namespace Bakabase.InsideWorld.Business.Components.Enhancer
{
    public class EnhancerService : IEnhancerService
    {
        private readonly ICustomPropertyService _customPropertyService;
        private readonly IResourceService _resourceService;
        private readonly ICustomPropertyValueService _customPropertyValueService;
        private readonly ConcurrentDictionary<int, IEnhancer> _enhancers;
        private readonly IEnhancementService _enhancementService;
        private readonly ICategoryEnhancerOptionsService _categoryEnhancerService;
        private readonly IStandardValueService _standardValueService;
        private readonly LogService _logService;
        private readonly IEnhancerDescriptors _enhancerDescriptors;
        private readonly IEnhancerLocalizer _enhancerLocalizer;

        public EnhancerService(ICustomPropertyService customPropertyService, IResourceService resourceService,
            ICustomPropertyValueService customPropertyValueService, IEnumerable<IEnhancer> enhancers,
            IEnhancementService enhancementService, ICategoryEnhancerOptionsService categoryEnhancerService,
            IStandardValueService standardValueService, LogService logService, IEnhancerLocalizer enhancerLocalizer,
            IEnhancerDescriptors enhancerDescriptors)
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
            _enhancers = new ConcurrentDictionary<int, IEnhancer>(enhancers.ToDictionary(d => d.Id, d => d));
        }

        protected async Task ApplyEnhancementsToResources(List<Enhancement> enhancements)
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
            var newPropertyAddModels = new List<(Enhancement Enhancement, CustomPropertyAddOrPutDto PropertyAddModel)>();
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
                if (!string.IsNullOrEmpty(enhancement.DynamicTarget) && targetOptions != null)
                {
                    if (targetDescriptor.IsDynamic)
                    {
                        var dynamicTargetOptions =
                            targetOptionsGroup.FirstOrDefault(x => x.DynamicTarget == enhancement.DynamicTarget);
                        if (dynamicTargetOptions == null)
                        {
                            if (targetOptions.AutoGenerateProperties == true)
                            {
                                dynamicTargetOptions = new EnhancerTargetFullOptions
                                {
                                    AutoGenerateProperties = true,
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
                            $"Found dynamic target {enhancement.DynamicTarget} for a static target: {targetDescriptor.Id}:{targetDescriptor.Name} of enhancer: {_enhancerDescriptors[enhancement.EnhancerId].Name}");
                    }
                }

                if (targetOptions == null)
                {
                    continue;
                }

                if (!targetOptions.PropertyId.HasValue && targetOptions.AutoGenerateProperties == true)
                {
                    var propertyCandidate = propertyMap.Values.FirstOrDefault(p =>
                        p.BizValueType == targetDescriptor.ValueType && p.Name == targetDescriptor.Name);
                    if (propertyCandidate == null)
                    {
                        var compatibleCustomPropertyTypes =
                            targetDescriptor.ValueType.GetCompatibleCustomPropertyTypes();
                        if (compatibleCustomPropertyTypes?.Any() == true)
                        {
                            newPropertyAddModels.Add((enhancement, new CustomPropertyAddOrPutDto
                            {
                                Name = targetDescriptor.Name,
                                Type = (int) compatibleCustomPropertyTypes[0]
                            }));
                        }
                    }
                    else
                    {
                        targetOptions.PropertyId = propertyCandidate.Id;
                    }

                    changedCategoryEnhancerOptions.Add(categoryOptions!);
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
                    var enhancement = newPropertyAddModels[i].Enhancement;
                    var property = properties[i];
                    enhancementTargetOptionsMap[enhancement].PropertyId = property.Id;
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

            var enhancerCustomPropertyValueLayerMap =
                _enhancers.ToDictionary(d => d.Key, d => _enhancerDescriptors[d.Key].PropertyValueScope);

            var pvs = new List<CustomPropertyValue>();
            var addedPvKeys = new HashSet<string>();
            var enhancementCpvMap = enhancements.ToDictionary(d => d, d => (CustomPropertyValue?) null);

            var changedProperties = new HashSet<CustomProperty>();

            foreach (var (enhancement, targetOptions) in enhancementTargetOptionsMap)
            {
                var property = propertyMap.GetValueOrDefault(targetOptions.PropertyId!.Value);
                if (property == null)
                {
                    continue;
                }
                
                // 
                // use first not null enhancement for enhancements with same properties;
                var pvKey =
                    $"{enhancement.ResourceId}-{property.Id}-{enhancerCustomPropertyValueLayerMap[enhancement.EnhancerId]}";
                if (addedPvKeys.Add(pvKey))
                {
                    var result = await _customPropertyValueService.Create(enhancement.Value, enhancement.ValueType,
                        property,
                        enhancement.ResourceId, enhancerCustomPropertyValueLayerMap[enhancement.EnhancerId]);
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
                }
            }

            // delete not relative property values;
            // add new property values;
            // update current property values;

            if (changedProperties.Any())
            {
                await _customPropertyService.UpdateRange(changedProperties.Select(p => p.ToDbModel()!).ToArray());
            }

            var valuesToUpdate = pvs.Where(pv => pv.Id > 0).ToList();
            var valuesToAdd = pvs.Except(valuesToUpdate).ToList();
            await _customPropertyValueService.UpdateRange(valuesToUpdate);
            await _customPropertyValueService.AddRange(valuesToAdd);
            var remainingPvKeys = pvs.Select(p => p.BizKey).ToHashSet();
            var valueIdsToDelete = currentPropertyValues.Where(cpv => !remainingPvKeys.Contains(cpv.BizKey))
                .Select(cpv => cpv.Id).ToList();
            await _customPropertyValueService.RemoveByKeys(valueIdsToDelete);

            foreach (var (e, v) in enhancementCpvMap)
            {
                e.CustomPropertyValueId = v?.Id ?? 0;
            }

            await _enhancementService.UpdateRange(enhancements);
        }

        protected async Task Enhance(List<Bakabase.Abstractions.Models.Domain.Resource> targetResources,
            HashSet<int>? restrictedEnhancerIds = null)
        {
            var categoryIds = targetResources.Select(c => c.CategoryId).ToHashSet();
            var categoryIdEnhancerOptionsMap =
                (await _categoryEnhancerService.GetAll(x => categoryIds.Contains(x.CategoryId)))
                .GroupBy(d => d.CategoryId)
                .ToDictionary(d => d.Key, d => d.ToList());
            var tasks = new Dictionary<IEnhancer, List<Bakabase.Abstractions.Models.Domain.Resource>>();
            foreach (var tr in targetResources)
            {
                var enhancerOptions = categoryIdEnhancerOptionsMap.GetValueOrDefault(tr.CategoryId);
                if (enhancerOptions != null)
                {
                    foreach (var eo in enhancerOptions)
                    {
                        if (restrictedEnhancerIds == null || restrictedEnhancerIds.Contains(eo.EnhancerId))
                        {
                            var enhancer = _enhancers.GetValueOrDefault(eo.EnhancerId);
                            if (enhancer != null)
                            {
                                if (!tasks.TryGetValue(enhancer, out var resources))
                                {
                                    tasks[enhancer] = resources = [];
                                }

                                resources.Add(tr);
                            }
                        }
                    }
                }
            }

            var resourceIds = targetResources.Select(r => r.Id).ToHashSet();
            var resourceIdEnhancerIdEnhancementsMap =
                (await _enhancementService.GetAll(x => resourceIds.Contains(x.ResourceId))).GroupBy(d => d.ResourceId)
                .ToDictionary(
                    d => d.Key,
                    d => d.GroupBy(c => c.EnhancerId).ToDictionary(e => e.Key, e => e.ToList()));

            var asyncTasks = new List<Task>();
            foreach (var (enhancer, resources) in tasks)
            {
                asyncTasks.Add(Task.Run(async () =>
                {
                    foreach (var resource in resources)
                    {
                        var dbEnhancements = resourceIdEnhancerIdEnhancementsMap.GetValueOrDefault(resource.Id)
                            ?.GetValueOrDefault(enhancer.Id) ?? [];
                        if (dbEnhancements.Any())
                        {
                            continue;
                        }

                        var enhancementRawValues = await enhancer.CreateEnhancements(resource);
                        if (enhancementRawValues?.Any() == true)
                        {
                            var enhancements = enhancementRawValues.Select(v =>
                                new Enhancement
                                {
                                    Target = v.Target,
                                    CreatedAt = DateTime.Now,
                                    EnhancerId = enhancer.Id,
                                    ResourceId = resource.Id,
                                    Value = v.Value,
                                    ValueType = v.ValueType
                                }).ToList();

                            var enhancementsToAdd = enhancements
                                .Except(dbEnhancements, Enhancement.BizKeyComparer)
                                .ToList();
                            var enhancementsToUpdate = enhancements.Except(enhancementsToAdd).ToList();
                            foreach (var e in enhancementsToUpdate)
                            {
                                e.Id = dbEnhancements.First(x => Enhancement.BizKeyComparer.Equals(x, e))
                                    .Id;
                            }

                            await _enhancementService.AddRange(enhancementsToAdd);
                            await _enhancementService.UpdateRange(enhancementsToUpdate);

                            await ApplyEnhancementsToResources(enhancements);
                        }
                    }
                }));
            }

            await Task.WhenAll(asyncTasks);
        }

        public async Task EnhanceResource(int resourceId, HashSet<int>? enhancerIds)
        {
            var resource = (await _resourceService.Get(resourceId, ResourceAdditionalItem.None))!;
            await Enhance([resource], enhancerIds);
        }

        public async Task EnhanceAll()
        {
            var resources = await _resourceService.GetAll(null, ResourceAdditionalItem.None);
            await Enhance(resources);
        }

        public async Task ReapplyEnhancementsToResources(int categoryId)
        {
            var resources = await _resourceService.GetAll(x => x.CategoryId == categoryId);
            var resourceIds = resources.Select(r => r.Id).ToList();
            var enhancements = await _enhancementService.GetAll(x => resourceIds.Contains(x.ResourceId));
            await ApplyEnhancementsToResources(enhancements);
        }
    }
}