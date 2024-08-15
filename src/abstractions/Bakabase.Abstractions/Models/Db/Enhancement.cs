using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db
{
    public record Enhancement
    {
        public int Id { get; set; }
        public int ResourceId { get; set; }
        public int EnhancerId { get; set; }
        public int Target { get; set; }
        public string? DynamicTarget { get; set; }
        public StandardValueType ValueType { get; set; }
        public string? Value { get; set; }
        public int CustomPropertyValueId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}