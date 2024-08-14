using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.ThirdParty.Bangumi.Models;

public record BangumiDetail()
{
    public string? CoverPath { get; set; }
    public string? Introduction { get; set; }
    public string? Name { get; set; }
    public List<TagValue>? Tags { get; set; }
    public decimal? Rating { get; set; }
    public Dictionary<string, List<string>>? OtherPropertiesInLeftPanel { get; set; }
}