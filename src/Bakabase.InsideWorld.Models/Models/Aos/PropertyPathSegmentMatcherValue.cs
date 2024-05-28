using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Components.Doc.Swagger;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
    [SwaggerCustomModel]
    public record PropertyPathSegmentMatcherValue
    {
        public string? FixedText { get; set; }
        public int? Layer { get; set; }
        public string? Regex { get; set; }
        public int PropertyId { get; set; }
        public bool IsReservedProperty { get; set; }
        public ResourceMatcherValueType ValueType { get; set; }
        [Obsolete] public string? Key { get; set; }
        [Obsolete] public ResourceProperty Property { get; set; }

        /// <summary>
        /// <see cref="ResourceProperty.ParentResource"/> or custom properties.
        /// </summary>
        public bool IsSecondaryProperty => !IsReservedProperty || PropertyId == (int) ResourceProperty.ParentResource;

        public bool IsResourceProperty => IsReservedProperty && PropertyId == (int) ResourceProperty.Resource;

        public bool IsValid => PropertyId > 0 && ValueType switch
        {
            ResourceMatcherValueType.Layer => Layer.HasValue && Layer != 0,
            ResourceMatcherValueType.Regex => Regex.IsNotEmpty(),
            ResourceMatcherValueType.FixedText => false,
            _ => throw new ArgumentOutOfRangeException()
        };

        public static PropertyPathSegmentMatcherValue BuildResourceAtFirstLayerAfterRootPath() =>
            new()
            {
                Layer = 1,
                ValueType = ResourceMatcherValueType.Layer,
                PropertyId = (int) ResourceProperty.Resource,
                IsReservedProperty = true
            };
    }
}