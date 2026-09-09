using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

/// <summary>
/// A part of the shared content the author is charging for.
/// </summary>
/// <param name="Url">Where buying it happens. Null when the site did not say.</param>
/// <param name="Price">What it costs, in whatever the site counts in. Null when it did not say.</param>
/// <param name="IsBought">Whether the user has already paid for it.</param>
public record SharedContentLock(string? Url, decimal? Price, bool IsBought);

public record PostContent
{
    public string Title { get; set; } = null!;
    public string MainHtml { get; set; } = null!;
    public List<string> CommentHtmlList { get; set; } = [];

    /// <summary>
    /// Parts of the content still behind a payment. Reading no longer buys them: spending money is
    /// a decision, and it belongs to whoever is asking for the content, not to the reader.
    /// </summary>
    public List<SharedContentLock> Locks { get; set; } = [];
}
