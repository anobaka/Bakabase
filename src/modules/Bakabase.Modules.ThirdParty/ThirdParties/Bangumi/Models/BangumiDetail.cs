using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bangumi.Models;

public record BangumiDetail()
{
    public string? CoverUrl { get; set; }
    public string? Introduction { get; set; }
    public string? Name { get; set; }
    public List<TagValue>? Tags { get; set; }
    public decimal? Rating { get; set; }
    public Dictionary<string, List<string>>? OtherPropertiesInLeftPanel { get; set; }

    /// <summary>
    /// The subject page this was read from. Carried because a match is worth recording: without
    /// it, an enhancer that just identified a work leaves nothing behind saying which work.
    /// </summary>
    public string? DetailUrl { get; set; }

    /// <summary>The subject id, out of <see cref="DetailUrl"/>. Null when there is no URL to read.</summary>
    public string? SubjectId => string.IsNullOrEmpty(DetailUrl)
        ? null
        : System.Text.RegularExpressions.Regex.Match(DetailUrl, @"subject/(\d+)") is {Success: true} m
            ? m.Groups[1].Value
            : null;
}