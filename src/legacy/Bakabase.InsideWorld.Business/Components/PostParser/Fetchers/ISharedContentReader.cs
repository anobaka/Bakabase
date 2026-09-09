using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

/// <summary>
/// Reads a place where someone shared a link — a forum thread, a page, a pasted block of text —
/// into text an extractor can work on.
/// <para>
/// It was <c>IPostContentFetcher</c>, and forum-shaped: one implementation per forum, picked by an
/// enum. Sharing is not that narrow. A reader now says what it can read, and the ones that know a
/// particular site outrank the ones that will read anything.
/// </para>
/// </summary>
public interface ISharedContentReader
{
    /// <summary>
    /// The forum this reader is for, when it is for one. The post-parser page still picks by source,
    /// so a site-specific reader keeps its enum; a general one has none.
    /// </summary>
    PostParserSource? Source { get; }

    /// <summary>
    /// Higher wins when more than one reader can read the same thing. A reader that knows a site
    /// beats one that will read any page, which in turn beats one that will read any text at all.
    /// </summary>
    int Priority { get; }

    /// <summary>Whether this reader can make sense of the reference at all.</summary>
    bool CanRead(string reference);

    Task<PostContent> ReadAsync(string reference, CancellationToken ct);
}

/// <summary>
/// The old name. Kept for one version so anything still typed against it compiles; implement
/// <see cref="ISharedContentReader"/> instead.
/// </summary>
[Obsolete("Renamed to ISharedContentReader.")]
public interface IPostContentFetcher : ISharedContentReader;
