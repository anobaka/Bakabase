using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

/// <summary>
/// Settings for the Javbus batch tool. The site itself (base url, cookie, user
/// agent) is configured once as an AV source in <see cref="AvSourceOptions"/> —
/// only the batching knobs live here.
/// </summary>
[Options(fileKey: "third-party-javbus-downloader")]
public class JavbusDownloaderOptions
{
    /// <summary>How many codes to fetch at once. Clamped to 1-8 when the batch runs.</summary>
    public int? Concurrency { get; set; }

    /// <summary>Pause after each fetch, to stay under the site's rate limit.</summary>
    public int? DelayMs { get; set; }

    /// <summary>
    /// How far below the largest candidate still counts as the same quality
    /// tier (percent). Only inside a tier do subtitle hints pick the winner.
    /// </summary>
    public int? SizeTolerancePercentage { get; set; }

    /// <summary>Whether covers are downloaded alongside the magnets.</summary>
    public bool? SaveCovers { get; set; }

    /// <summary>Where covers land. Required once <see cref="SaveCovers"/> is on.</summary>
    public string? CoverDirectory { get; set; }
}
