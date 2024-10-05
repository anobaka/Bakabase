using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Models.Input;

public record CustomPropertyTypeConversionExampleInputModel
{
    public List<T>? TestData { get; set; }

    public record T
    {
        public PropertyType Type { get; set; }
        public string? SerializedBizValue { get; set; }
    }
}