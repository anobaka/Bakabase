using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Conversion;
using Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Modules.CustomProperty.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public class EnhancerService
    {
        private readonly CustomPropertyService _customPropertyService;
        private readonly PropertyValueConverter _propertyValueConverter;
        private readonly ResourceService _resourceService;
        private readonly CustomPropertyValueService _customPropertyValueService;
        private readonly ConcurrentDictionary<EnhancerId, IEnhancer> _enhancers;
        private readonly EnhancementRecordService _recordService;
        private readonly CategoryEnhancerService _categoryEnhancerService;

        public EnhancerService(CustomPropertyService customPropertyService,
            PropertyValueConverter propertyValueConverter, ResourceService resourceService,
            CustomPropertyValueService customPropertyValueService, IEnumerable<IEnhancer> enhancers,
            EnhancementRecordService recordService, CategoryEnhancerService categoryEnhancerService)
        {
            _customPropertyService = customPropertyService;
            _propertyValueConverter = propertyValueConverter;
            _resourceService = resourceService;
            _customPropertyValueService = customPropertyValueService;
            _recordService = recordService;
            _categoryEnhancerService = categoryEnhancerService;
            _enhancers = new ConcurrentDictionary<EnhancerId, IEnhancer>(enhancers.ToDictionary(d => d.Id, d => d));
        }

        protected async Task Enhance(EnhancementTask task)
        {
            var enhancements = await task.Enhancer.Enhance(task.Resource, null);
            if (enhancements?.Any() == true)
            {
                var pvs = new List<CustomPropertyValue>();
                foreach (var enhancement in enhancements)
                {
                    var p = task.TargetPropertyMap[enhancement.Target];
                    var pv = await _propertyValueConverter.Convert(enhancement.ValueType, enhancement.Value, p);
                    pvs.Add(pv);
                }

                var valuesToUpdate = pvs.Where(pv =>
                    task.CurrentValues?.Any(d => CustomPropertyValue.BizKeyComparer.Equals(pv, d)) == true).ToList();
                var valuesToAdd = pvs.Except(valuesToUpdate).ToList();
                await _customPropertyValueService.UpdateRange(valuesToUpdate);
                await _customPropertyValueService.AddRange(valuesToAdd);
            }

            var record = new EnhancementRecord(task.Resource.Id, task.Enhancer.Id, enhancements);
            await _recordService.Add(record);
        }

        protected async Task Enhance(
            List<Business.Models.Domain.Resource> targetResources)
        {
            var categoryIdResourcesMap =
                targetResources.GroupBy(d => d.CategoryId).ToDictionary(d => d.Key, d => d.ToList());
            var categoryIds = categoryIdResourcesMap.Keys.ToHashSet();
            var resourceIdPropertyValuesMap =
                (await _customPropertyValueService.GetAll(null, CustomPropertyValueAdditionalItem.None, true))
                .GroupBy(d => d.ResourceId)
                .ToDictionary(d => d.Key, d => d.ToList());
            var propertyMap = (await _customPropertyService.GetAll()).ToDictionary(d => d.Id, d => d);
            var categoryIdEnhancerOptionsMap =
                (await _categoryEnhancerService.GetAll(x => categoryIds.Contains(x.CategoryId)))
                .GroupBy(d => d.CategoryId)
                .ToDictionary(d => d.Key, d => d.ToList());
            var resourceIds = targetResources.Select(r => r.Id).ToHashSet();
            var resourceIdEnhancerIdEnhancementRecordsMap =
                (await _recordService.GetAll(x => resourceIds.Contains(x.ResourceId))).GroupBy(d => d.ResourceId)
                .ToDictionary(
                    d => d.Key,
                    d => d.GroupBy(c => c.EnhancerId).ToDictionary(e => e.Key, e => e.First()));

            var tasks = new List<EnhancementTask>();
            foreach (var (cId, resources) in categoryIdResourcesMap)
            {
                var enhancerOptions = categoryIdEnhancerOptionsMap.GetValueOrDefault(cId);
                if (enhancerOptions != null)
                {
                    foreach (var eo in enhancerOptions)
                    {
                        var enhancer = _enhancers.GetValueOrDefault(eo.EnhancerId);
                        if (enhancer != null)
                        {
                            foreach (var resource in resources)
                            {
                                var record = resourceIdEnhancerIdEnhancementRecordsMap.GetValueOrDefault(resource.Id)
                                    ?.GetValueOrDefault(enhancer.Id);
                                if (record == null)
                                {
                                    var targetPropertyMap = eo.TargetPropertyIdMap
                                        ?.ToDictionary(x => x.Key, x => propertyMap.GetValueOrDefault(x.Value))
                                        .Where(x => x.Value != null)
                                        .ToDictionary(d => d.Key, d => d.Value);
                                    if (targetPropertyMap?.Any() == true)
                                    {
                                        tasks.Add(new EnhancementTask(resource, enhancer, eo, targetPropertyMap!,
                                            resourceIdPropertyValuesMap.GetValueOrDefault(resource.Id)));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var enhancerTasksGroups = tasks.GroupBy(d => d.Enhancer);

            var asyncTasks = new List<Task>();
            foreach (var enhancerTasks in enhancerTasksGroups)
            {
                asyncTasks.Add(Task.Run(async () =>
                {
                    foreach (var et in enhancerTasks)
                    {
                        await Enhance(et);
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
    }
}