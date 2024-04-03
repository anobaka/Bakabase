namespace Bakabase.Modules.CustomProperty.Properties.Multilevel
{
    public record MultilevelDataOptions
    {
        public string Id { get; set; } = null!;
        public string Value { get; set; } = null!;
        public string Color { get; set; } = null!;
        public MultilevelDataOptions[]? Children { get; set; }
    }
}