using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.LocaleEmulator.Models
{
    public record LocaleEmulatorVersion : DependentComponentVersion
    {
        public string DownloadUrl { get; set; } = null!;
    }
}
