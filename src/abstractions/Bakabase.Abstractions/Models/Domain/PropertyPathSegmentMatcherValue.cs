using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Doc.Swagger;
using Bootstrap.Extensions;

namespace Bakabase.Abstractions.Models.Domain
{
    [SwaggerCustomModel]
    public record PropertyPathSegmentMatcherValue
    {
        public string? FixedText { get; set; }
        public int? Layer { get; set; }
        public string? Regex { get; set; }
        public int PropertyId { get; set; }
        public bool IsCustomProperty { get; set; }
        public ResourceMatcherValueType ValueType { get; set; }
        public string? PropertyName { get; set; }

        /// <summary>
        /// <see cref="ResourceProperty.ParentResource"/> or custom properties.
        /// </summary>
        public bool IsSecondaryProperty => IsCustomProperty || PropertyId == (int) ResourceProperty.ParentResource ||
                                           PropertyId == (int) ResourceProperty.Rating ||
                                           PropertyId == (int) ResourceProperty.Introduction;

        public bool IsResourceProperty => !IsCustomProperty && PropertyId == (int) ResourceProperty.Resource;


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
                IsCustomProperty = false
            };
    }
}