namespace Bakabase.Modules.PostParser.Models.Domain;

/// <summary>Readable post or pasted text, independent of tasks, resources and workflows.</summary>
public record PostContent
{
    public string Title { get; init; } = "";
    public string MainHtml { get; init; } = "";
    public List<string> CommentHtmlList { get; init; } = [];
    public List<PostComment> Comments { get; init; } = [];
    public List<PostContentLock> Locks { get; init; } = [];
    public string? SourceUrl { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public string Scope { get; init; } = "firstPage";
    public decimal? Balance { get; init; }

    /// <summary>The selected platform reader, when the content has a known source.</summary>
    public string? SourceHint { get; init; }
}

/// <summary>Describes restricted content; reading a post never purchases it.</summary>
public record PostContentLock(string? Url, decimal? Price, bool IsBought)
{
    public string? Id { get; init; }
    public string? Floor { get; init; }
}

public record PostComment
{
    public string? Id { get; init; }
    public string? Floor { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PostedAt { get; init; }
    public string Html { get; init; } = "";
}
