namespace Bakabase.Modules.CustomProperty.Components.Properties.Multilevel
{
    public record MultilevelDataOptions
    {
        public string Value { get; set; } = null!;
        public string Label { get; set; } = null!;
        public string Color { get; set; } = null!;
        public List<MultilevelDataOptions>? Children { get; set; }
    }
}