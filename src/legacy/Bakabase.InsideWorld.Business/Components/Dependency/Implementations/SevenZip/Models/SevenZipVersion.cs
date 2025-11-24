using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip.Models
{
    public record SevenZipVersion : DependentComponentVersion
    {
        public string DownloadUrl { get; set; } = null!;
    }
}
