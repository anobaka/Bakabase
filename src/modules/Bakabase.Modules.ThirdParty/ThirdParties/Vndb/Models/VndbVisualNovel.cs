using System.Text.Json.Serialization;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Vndb.Models;

/// <summary>A cover image, as VNDB's API returns it.</summary>
public record VndbImage
{
    public string? Url { get; set; }
}

/// <summary>One visual novel related to another, and how.</summary>
public record VndbRelation
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    /// <summary>VNDB's own word: <c>seq</c>, <c>preq</c>, <c>side</c>, <c>ser</c>, <c>fan</c>…</summary>
    public string? Relation { get; set; }

    /// <summary>False for fan works and unofficial ports, which are rarely what a series means.</summary>
    [JsonPropertyName("relation_official")]
    public bool RelationOfficial { get; set; }

    public VndbImage? Image { get; set; }
}

/// <summary>A visual novel.</summary>
public record VndbVisualNovel
{
    /// <summary>VNDB's id, <c>v17</c> shaped — the <c>v</c> is part of it.</summary>
    public string Id { get; set; } = "";

    public string? Title { get; set; }

    /// <summary>The original-language title, when the main one has been romanised.</summary>
    [JsonPropertyName("alttitle")]
    public string? AltTitle { get; set; }

    public string? Released { get; set; }

    public VndbImage? Image { get; set; }

    public List<VndbRelation>? Relations { get; set; }

    public string Url => $"https://vndb.org/{Id}";

    /// <summary>What to call it: the romanised title if there is one, else whatever VNDB has.</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(Title) ? Title!
        : !string.IsNullOrWhiteSpace(AltTitle) ? AltTitle!
        : Id;
}

/// <summary>One page of a VNDB query.</summary>
public record VndbQueryResponse
{
    public List<VndbVisualNovel> Results { get; set; } = [];

    /// <summary>Whether asking for the next page would return anything.</summary>
    public bool More { get; set; }
}
