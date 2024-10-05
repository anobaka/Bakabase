using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Models.View;

public record CustomPropertyTypeConversionExampleViewModel
{
    public List<Tin>? Results { get; set; }

    public record Tin
    {
        public PropertyType Type { get; set; }
        public StandardValueType BizValueType { get; set; }
        public string? SerializedBizValue { get; set; }
        public List<Tout>? Outputs { get; set; }
    }

    public record Tout
    {
        public PropertyType Type { get; set; }
        public StandardValueType BizValueType { get; set; }
        public string? SerializedBizValue { get; set; }
    }
}