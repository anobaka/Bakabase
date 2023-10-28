using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg.Models
{
    record VersionRsp
    {
        public string Version { get; set; } = null!;
        public string Permalink { get; set; } = null!;
        public Dictionary<string, NameAndUrls> Bin { get; set; } = null!;

        public record NameAndUrls
        {
            [JsonProperty("ffmpeg")] public string FfMpeg { get; set; } = null!;
            [JsonProperty("ffprobe")] public string FfProbe { get; set; } = null!;
            [JsonProperty("ffplay")] public string? FfPlay { get; set; }
        }
    }
}