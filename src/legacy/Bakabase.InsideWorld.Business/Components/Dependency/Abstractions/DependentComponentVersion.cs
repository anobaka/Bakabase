namespace Bakabase.InsideWorld.Business.Components.Dependency.Abstractions
{
    public record DependentComponentVersion
    {
        public string Version { get; set; } = null!;
        public string? Description { get; set; }
        public bool CanUpdate { get; set; }

        public static DependentComponentVersion Unknown => new DependentComponentVersion();
    }
}