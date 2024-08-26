using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.Abstractions.Models.Domain
{
    public record CustomProperty
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int Type { get; set; }
        public StandardValueType DbValueType { get; set; }
        public StandardValueType BizValueType { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<Category>? Categories { get; set; }
        public object? Options { get; set; }
        public int? ValueCount { get; set; }
    }
}