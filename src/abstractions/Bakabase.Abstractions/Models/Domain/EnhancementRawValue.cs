using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain
{
    public record EnhancementRawValue
    {
        public int Target { get; set; }
        public string? DynamicTarget { get; set; }
        public object? Value { get; set; }
        public StandardValueType ValueType { get; set; }
    }
}