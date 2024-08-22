using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg.Models
{
    record FfMpegVersion : DependentComponentVersion
    {
        public string FfMpegUrl { get; set; } = null!;
        public string FfProbeUrl { get; set; } = null!;
        public string? FfPlayUrl { get; set; }
    }
}
