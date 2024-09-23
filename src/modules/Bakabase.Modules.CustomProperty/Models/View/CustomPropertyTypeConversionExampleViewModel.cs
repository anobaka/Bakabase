using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Models.View;

public record CustomPropertyTypeConversionExampleViewModel
{
    public List<Tin>? Results { get; set; }

    public record Tin
    {
        public CustomPropertyType Type { get; set; }
        public StandardValueType BizValueType { get; set; }
        public string? SerializedBizValue { get; set; }
        public List<Tout>? Outputs { get; set; }
    }

    public record Tout
    {
        public CustomPropertyType Type { get; set; }
        public StandardValueType BizValueType { get; set; }
        public string? SerializedBizValue { get; set; }
    }
}