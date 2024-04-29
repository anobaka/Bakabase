using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Conversion;
using Bakabase.InsideWorld.Business.Components.StandardValue;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using EnhancerAttribute = Bakabase.Modules.Enhancer.Abstractions.Attributes.EnhancerAttribute;
using IEnhancer = Bakabase.Abstractions.Components.Enhancer.IEnhancer;

namespace Bakabase.InsideWorld.Business.Components.Enhancer
{
    public class EnhancerService : IEnhancerService
    {
        private readonly ICustomPropertyService _customPropertyService;
        private readonly PropertyValueConverter _propertyValueConverter;
        private readonly ResourceService _resourceService;
        private readonly CustomPropertyValueService _customPropertyValueService;
        private readonly ConcurrentDictionary<int, IEnhancer> _enhancers;
        private readonly IEnhancementService _enhancementService;
        private readonly ICategoryEnhancerOptionsService _categoryEnhancerService;
        private readonly StandardValueService _standardValueService;

        public EnhancerService(ICustomPropertyService customPropertyService,
            PropertyValueConverter propertyValueConverter, ResourceService resourceService,
            CustomPropertyValueService customPropertyValueService, IEnumerable<IEnhancer> enhancers,
            IEnhancementService enhancementService, ICategoryEnhancerOptionsService categoryEnhancerService,
            StandardValueService standardValueService)
        {
            _customPropertyService = customPropertyService;
            _propertyValueConverter = propertyValueConverter;
            _resourceService = resourceService;
            _customPropertyValueService = customPropertyValueService;
            _enhancementService = enhancementService;
            _categoryEnhancerService = categoryEnhancerService;
            _standardValueService = standardValueService;
            _enhancers = new ConcurrentDictionary<int, IEnhancer>(enhancers.ToDictionary(d => d.Id, d => d));
        }

        protected async Task ApplyEnhancementsToResources(List<Bakabase.Abstractions.Models.Domain.Enhancement> enhancements)
        {
            var resourceIds = enhancements.Select(s => s.ResourceId).ToHashSet();
            var resourceMap = (await _resourceService.GetByKeys(resourceIds.ToArray())).ToDictionary(d => d.Id, d => d);
            var categoryIds = resourceMap.Values.Select(v => v.CategoryId).ToHashSet();
            var enhancerIds = enhancements.Select(s => s.EnhancerId).ToHashSet();
            var enhancerOptions = await _categoryEnhancerService.GetAll(x =>
                enhancerIds.Contains(x.EnhancerId) && categoryIds.Contains(x.CategoryId));
            var categoryEnhancerOptionsMap = enhancerOptions
                .GroupBy(d => d.CategoryId)
                .ToDictionary(d => d.Key,
                    d => d
                        .ToDictionary(c => c.EnhancerId, c => c));
            var propertyIds = enhancerOptions.Where(o => o.Options?.TargetOptionsMap != null)
                .SelectMany(o => o.Options?.TargetOptionsMap!.Select(c => c.Value.PropertyId)!).ToHashSet();
            var propertyMap =
                (await _customPropertyService.GetByKeys(propertyIds, CustomPropertyAdditionalItem.None, false))
                .ToDictionary(d => d.Id, d => d);
            var currentPropertyValues =
                await _customPropertyValueService.GetAll(x => resourceIds.Contains(x.ResourceId),
                    CustomPropertyValueAdditionalItem.None, true);

            var enhancementTargetOptionsMap = new Dictionary<Bakabase.Abstractions.Models.Domain.Enhancement, EnhancerTargetOptions>();
            foreach (var enhancement in enhancements)
            {
                var resource = resourceMap.GetValueOrDefault(enhancement.ResourceId);
                if (resource == null)
                {
                    continue;
                }

                var targetOptions = categoryEnhancerOptionsMap
                    .GetValueOrDefault(resource.CategoryId)
                    ?.GetValueOrDefault(enhancement.EnhancerId)?.Options?.TargetOptionsMap
                    ?.GetValueOrDefault(enhancement.Target);
                if (targetOptions == null)
                {
                    continue;
                }

                enhancementTargetOptionsMap[enhancement] = targetOptions;
            }

            var enhancementsIntegratedWithAlias = enhancementTargetOptionsMap
                .Where(v => v.Value.IntegrateWithAlias && v.Key.Value != null)
                .Select(v => v.Key).ToList();
            if (enhancementsIntegratedWithAlias.Any())
            {
                await _standardValueService.IntegrateWithAlias(
                    enhancementsIntegratedWithAlias.ToDictionary(d => d.Value!, d => d.ValueType));
            }

            var enhancerCustomPropertyValueLayerMap = _enhancers.ToDictionary(d => d.Key,
                d => d.Value.GetType().GetRequiredAttribute<EnhancerAttribute>().CustomPropertyValueLayer);

            var pvs = new List<CustomPropertyValue>();
            foreach (var (enhancement, targetOptions) in enhancementTargetOptionsMap)
            {
                var property = propertyMap.GetValueOrDefault(targetOptions.PropertyId);

                if (property == null)
                {
                    continue;
                }

                var pv = await _propertyValueConverter.Convert(enhancement.ValueType, enhancement.Value, property);
                pv.Layer = enhancerCustomPropertyValueLayerMap[enhancement.EnhancerId];
                pvs.Add(pv);
            }

            var valuesToUpdate = pvs.Where(pv =>
                currentPropertyValues.Any(d => CustomPropertyValue.BizKeyComparer.Equals(pv, d))).ToList();
            var valuesToAdd = pvs.Except(valuesToUpdate).ToList();
            await _customPropertyValueService.UpdateRange(valuesToUpdate);
            await _customPropertyValueService.AddRange(valuesToAdd);
        }

        protected async Task Enhance(List<Models.Domain.Resource> targetResources)
        {
            var categoryIds = targetResources.Select(c => c.CategoryId).ToHashSet();
            var categoryIdEnhancerOptionsMap =
                (await _categoryEnhancerService.GetAll(x => categoryIds.Contains(x.CategoryId)))
                .GroupBy(d => d.CategoryId)
                .ToDictionary(d => d.Key, d => d.ToList());
            var tasks = new Dictionary<IEnhancer, List<Models.Domain.Resource>>();
            foreach (var tr in targetResources)
            {
                var enhancerOptions = categoryIdEnhancerOptionsMap.GetValueOrDefault(tr.CategoryId);
                if (enhancerOptions != null)
                {
                    foreach (var eo in enhancerOptions)
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
                            var enhancements = enhancementRawValues.Select(v => new Bakabase.Abstractions.Models.Domain.Enhancement
                            {
                                Target = v.Target,
                                CreatedAt = DateTime.Now,
                                EnhancerId = enhancer.Id,
                                ResourceId = resource.Id,
                                Value = v.Value,
                                ValueType = v.ValueType
                            }).ToList();

                            var enhancementsToAdd = enhancements
                                .Except(dbEnhancements, Bakabase.Abstractions.Models.Domain.Enhancement.BizKeyComparer).ToList();
                            var enhancementsToUpdate = enhancements.Except(enhancementsToAdd).ToList();
                            foreach (var e in enhancementsToUpdate)
                            {
                                e.Id = dbEnhancements.First(x => Bakabase.Abstractions.Models.Domain.Enhancement.BizKeyComparer.Equals(x, e))
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

        public async Task EnhanceResource(int resourceId)
        {
            var resource = (await _resourceService.GetByKey(resourceId, ResourceAdditionalItem.None, false))!;
            await Enhance([resource]);
        }

        public async Task EnhanceAll()
        {
            var resources = await _resourceService.GetAll(ResourceAdditionalItem.None);
            await Enhance(resources);
        }

        public async Task ReapplyEnhancementsToResources(int categoryId)
        {
            var resources = await _resourceService.GetAllEntities(x => x.CategoryId == categoryId);
            var resourceIds = resources.Select(r => r.Id).ToList();
            var enhancements = await _enhancementService.GetAll(x => resourceIds.Contains(x.ResourceId));
            await ApplyEnhancementsToResources(enhancements);
        }
    }
}