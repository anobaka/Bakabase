using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db
{
    public record EnhancementDbModel
    {
        public int Id { get; set; }
        public int ResourceId { get; set; }
        public int EnhancerId { get; set; }
        public int Target { get; set; }
        public string? DynamicTarget { get; set; }
        public StandardValueType ValueType { get; set; }
        public string? Value { get; set; }

        /// <summary>
        /// This field will be filled after converting enhancement raw value to property value
        /// </summary>
        public PropertyPool? PropertyPool { get; set; }

        /// <summary>
        /// <inheritdoc cref="PropertyPool"/>
        /// </summary>
        public int? PropertyId { get; set; }
        public string Key { get; set; }= null!;
    }
}