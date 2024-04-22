using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Conversion;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public class EnhancerService
    {
        public Dictionary<int, CategoryEnhancer> CategoryEnhancers { get; set; } = [];
        private readonly Dictionary<StandardValueType, IStandardValueHandler> _standardValueHandlers;
        private readonly CustomPropertyService _customPropertyService;
        private readonly PropertyValueConverter _propertyValueConverter;

        public EnhancerService(IEnumerable<IStandardValueHandler> standardValueHandlers, CustomPropertyService customPropertyService, PropertyValueConverter propertyValueConverter)
        {
            _customPropertyService = customPropertyService;
            _propertyValueConverter = propertyValueConverter;
            _standardValueHandlers = standardValueHandlers.ToDictionary(d => d.Type, d => d);
        }

        public async Task GenerateEnhancements(Bakabase.Abstractions.Models.Domain.Resource resource, IEnhancer enhancer,
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
        }
    }

    public record CategoryEnhancer
    {
        public Type Enhancer { get; set; }
        public CategoryEnhancerOptions? Options { get; set; }
    }

    public record CategoryEnhancerOptions
    {
        public Dictionary<int, int> TargetPropertyIdMap { get; set; } = [];
    }
}