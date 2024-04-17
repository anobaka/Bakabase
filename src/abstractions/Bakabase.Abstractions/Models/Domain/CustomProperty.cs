using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.Abstractions.Models.Domain
{
    public record CustomProperty
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public CustomPropertyType Type { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<Category>? Categories { get; set; }
        public object? Options { get; set; }
    }

    public record CustomProperty<T> : CustomProperty
    {
        private T? _options;

        public new T? Options
        {
            get => _options;
            set => base.Options = _options = value;
        }
    }
}
