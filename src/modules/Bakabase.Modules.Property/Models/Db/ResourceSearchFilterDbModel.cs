using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Models.Db
{
    public record ResourceSearchFilterDbModel
    {
        public PropertyPool PropertyPool { get; set; }
        public int PropertyId { get; set; }
        public SearchOperation Operation { get; set; }

        /// <summary>
        /// Serialized
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// Redundancy 
        /// </summary>
        public StandardValueType ValueType { get; set; }
    }
}