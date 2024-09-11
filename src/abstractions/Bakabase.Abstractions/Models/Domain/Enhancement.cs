using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain
{
    public record Enhancement
    {
        public int Id { get; set; }
        public int ResourceId { get; set; }
        public int EnhancerId { get; set; }
        public StandardValueType ValueType { get; set; }

        /// <summary>
        /// <see cref="EnhancementRawValue.Target"/>
        /// </summary>
        public int Target { get; set; }

        public string? DynamicTarget { get; set; }

        /// <summary>
        /// <see cref="EnhancementRawValue.Value"/>
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// <inheritdoc cref="Models.Db.Enhancement.PropertyType"/>
        /// </summary>
        public ResourcePropertyType? PropertyType { get; set; }

        /// <summary>
        /// <inheritdoc cref="PropertyType"/>
        /// </summary>
        public int? PropertyId { get; set; }

        public CustomPropertyValue? CustomPropertyValue { get; set; }
        public ReservedPropertyValue? ReservedPropertyValue { get; set; }
        public Property? Property { get; set; }
    }
}