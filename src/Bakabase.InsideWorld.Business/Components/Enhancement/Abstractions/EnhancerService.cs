using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Conversion;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.CustomProperty.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public class EnhancerService
    {
        public Dictionary<int, CategoryEnhancer> CategoryEnhancers { get; set; } = [];
        private readonly Dictionary<StandardValueType, IStandardValueHandler> _standardValueHandlers;
        private readonly CustomPropertyService _customPropertyService;
        private readonly PropertyValueConverter _propertyValueConverter;
        private readonly ResourceCategoryService _categoryService;
        private readonly ResourceService _resourceService;
        private readonly CustomPropertyValueService _customPropertyValueService;
        private readonly ConcurrentDictionary<EnhancerId, IEnhancer> _enhancers;

        public EnhancerService(IEnumerable<IStandardValueHandler> standardValueHandlers,
            CustomPropertyService customPropertyService, PropertyValueConverter propertyValueConverter,
            ResourceCategoryService categoryService, ResourceService resourceService,
            CustomPropertyValueService customPropertyValueService, IEnumerable<IEnhancer> enhancers)
        {
            _customPropertyService = customPropertyService;
            _propertyValueConverter = propertyValueConverter;
            _categoryService = categoryService;
            _resourceService = resourceService;
            _customPropertyValueService = customPropertyValueService;
            _standardValueHandlers = standardValueHandlers.ToDictionary(d => d.Type, d => d);
            _enhancers = new ConcurrentDictionary<EnhancerId, IEnhancer>(enhancers.ToDictionary(d => d.Id, d => d));
        }

        public async Task<List<CustomPropertyValue>> GenerateEnhancements(Bakabase.Abstractions.Models.Domain.Resource resource,
            IEnhancer enhancer,
            CategoryEnhancerOptions options)
        {
            var properties = (await _customPropertyService.GetAll())
                .Where(s => options.TargetPropertyIdMap.Values.Contains(s.Id)).ToDictionary(d => d.Id, d => d);

            var pvs = new List<CustomPropertyValue>();

            var enhancements = await enhancer.Enhance(resource, options);
            if (enhancements?.Any() == true)
            {
                foreach (var enhancement in enhancements)
                {
                    var p = properties[options.TargetPropertyIdMap[enhancement.Target]];
                    var pv = await _propertyValueConverter.Convert(enhancement.ValueType, enhancement.Value, p);
                    pvs.Add(pv);
                }
            }

            return pvs;
        }

        public async Task EnhanceAll()
        {
            var categories = await _categoryService.GetAllDto();
            var allResources = (await _resourceService.GetAll(ResourceAdditionalItem.None)).GroupBy(d => d.CategoryId)
                .ToDictionary(d => d.Key, d => d.ToList());
            var allCustomPropertyValues = (await _customPropertyValueService.GetAll()).GroupBy(d => d.ResourceId)
                .ToDictionary(d => d.Key, d => d.ToList());
            var categoryEnhancers = new List<CategoryEnhancer>();
            var enhancementRecords = new Dictionary<int, EnhancementRecord>();
            foreach (var ce in categoryEnhancers)
            {
                var enhancer = _enhancers[ce.EnhancerId];
                var resources = allResources[ce.CategoryId];
                foreach (var resource in resources)
                {
                    if (!enhancementRecords.ContainsKey(resource.Id))
                    {
                        var pvs = await GenerateEnhancements(resource, enhancer, ce.Options!);
                        // add or update values
                        // build and save records
                    }
                }
            }
        }
    }

    public record CategoryEnhancer
    {
        public int CategoryId { get; set; }
        public EnhancerId EnhancerId { get; set; }
        public CategoryEnhancerOptions? Options { get; set; }
    }

    public record CategoryEnhancerOptions
    {
        public Dictionary<int, int> TargetPropertyIdMap { get; set; } = [];
    }
}