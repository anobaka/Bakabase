using System.Collections.Generic;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

public class AvSourceConfig
{
    public bool? Enabled { get; set; }

    public string? BaseUrl { get; set; }

    public string? Cookie { get; set; }

    public string? UserAgent { get; set; }
}

[Options(fileKey: "third-party-av-sources")]
public class AvSourceOptions
{
    public Dictionary<string, AvSourceConfig>? Sources { get; set; }

    /// <summary>
    /// Per-target ordered source preference. Key is the int value of AvEnhancerTarget;
    /// value is the ordered list of source ids the AV enhancer should walk when picking
    /// a value for that target. Targets absent from this map use the built-in source order.
    /// </summary>
    public Dictionary<int, List<string>>? PreferredSourcesByTarget { get; set; }
}
