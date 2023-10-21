namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Installer
{
    public record SimpleVersion
    {
        public string Version { get; set; } = null!;
        public string? Description { get; set; }
    }
}