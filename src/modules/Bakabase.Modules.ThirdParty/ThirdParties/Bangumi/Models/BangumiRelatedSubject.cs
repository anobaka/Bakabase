using System.Text.Json.Serialization;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bangumi.Models;

/// <summary>
/// One subject bgm.tv says is related to another, as its API returns it.
/// </summary>
public record BangumiRelatedSubject
{
    [JsonPropertyName("id")] public int Id { get; set; }

    /// <summary>The original-language name, which is what the site itself leads with.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>The Chinese name, when someone has supplied one.</summary>
    [JsonPropertyName("name_cn")] public string? NameCn { get; set; }

    /// <summary>How it is related — "续集", "系列", "书籍", and so on, in Chinese as published.</summary>
    [JsonPropertyName("relation")] public string? Relation { get; set; }

    [JsonPropertyName("images")] public BangumiRelatedSubjectImages? Images { get; set; }

    /// <summary>The name to show: the Chinese one where there is one, else the original.</summary>
    public string? DisplayName => string.IsNullOrWhiteSpace(NameCn) ? Name : NameCn;

    public string Url => $"https://bgm.tv/subject/{Id}";
}

public record BangumiRelatedSubjectImages
{
    [JsonPropertyName("large")] public string? Large { get; set; }
    [JsonPropertyName("common")] public string? Common { get; set; }
    [JsonPropertyName("medium")] public string? Medium { get; set; }
}
