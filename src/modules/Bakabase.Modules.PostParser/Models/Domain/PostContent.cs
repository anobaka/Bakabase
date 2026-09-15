namespace Bakabase.Modules.PostParser.Models.Domain;

/// <summary>Readable post or pasted text, independent of tasks, resources and workflows.</summary>
public record PostContent
{
    public string Title { get; init; } = "";
    public string MainHtml { get; init; } = "";
    public List<string> CommentHtmlList { get; init; } = [];
    public List<PostContentLock> Locks { get; init; } = [];

    /// <summary>The selected platform reader, when the content has a known source.</summary>
    public string? SourceHint { get; init; }
}

/// <summary>Describes restricted content; reading a post never purchases it.</summary>
public record PostContentLock(string? Url, decimal? Price, bool IsBought);
