using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Models.Input;

public record CustomPropertyTypeConversionExampleInputModel
{
    public List<T>? TestData { get; set; }

    public record T
    {
        public CustomPropertyType Type { get; set; }
        public string? SerializedBizValue { get; set; }
    }
}