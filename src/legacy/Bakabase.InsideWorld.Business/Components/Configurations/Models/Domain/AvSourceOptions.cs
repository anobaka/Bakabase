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
}
