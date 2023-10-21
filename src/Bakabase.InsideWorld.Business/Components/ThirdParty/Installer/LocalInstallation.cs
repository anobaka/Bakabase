namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Installer
{
    public record LocalInstallation
    {
        public string? Location { get; set; }
        public string Version { get; set; } = null!;
    }
}