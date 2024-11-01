namespace Bakabase.InsideWorld.Business.Components.Dependency.Abstractions
{
    public record DependentComponentContext
    {
        // public DependentComponentStatus Status { get; set; }
        public string? Error { get; set; }
        public int InstallationProgress { get; set; }
        public string? Version { get; set; }
        public string? Location { get; set; }
        public bool IsRequired { get; set; }
    }
}