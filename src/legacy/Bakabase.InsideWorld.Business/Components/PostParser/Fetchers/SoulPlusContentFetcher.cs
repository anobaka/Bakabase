using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using CsQuery;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

public class SoulPlusContentFetcher(SoulPlusClient spClient) : ISharedContentReader
{
    public PostParserSource? Source => PostParserSource.SoulPlus;

    /// <summary>It knows the site's markup; anything general should lose to it.</summary>
    public int Priority => 100;

    public bool CanRead(string reference) =>
        Uri.TryCreate(reference, UriKind.Absolute, out var uri) &&
        uri.Host.Contains("soulplus", StringComparison.OrdinalIgnoreCase);

    public async Task<PostContent> ReadAsync(string reference, CancellationToken ct)
    {
        // Reading no longer buys anything. What is locked comes back described, and whoever asked
        // for the content decides whether to spend — see ISharedContentPurchaser.
        var post = await spClient.GetPostAsync(reference, ct);

        var cq = new CQ(post.Html);
        var mainContent = WebUtility.HtmlDecode(cq["#read_tpc"].Html());
        var commentContents = cq[".tpc_content>.f14:not(#read_tpc)"]
            .Select(c => WebUtility.HtmlDecode(c.Cq().Html()))
            .ToList();

        return new PostContent
        {
            Title = post.Title,
            MainHtml = mainContent,
            CommentHtmlList = commentContents,
            Locks = post.LockedContents?
                .Select(l => new SharedContentLock(l.Url, l.Price, l.IsBought))
                .ToList() ?? []
        };
    }
}

/// <summary>
/// Pays for a locked part of shared content. Separate from reading because the two happen at
/// different moments and under different authority: reading is free and automatic, paying is a
/// decision with a limit attached.
/// </summary>
public interface ISharedContentPurchaser
{
    PostParserSource Source { get; }

    Task BuyAsync(string lockUrl, CancellationToken ct);
}

public class SoulPlusPurchaser(SoulPlusClient spClient) : ISharedContentPurchaser
{
    public PostParserSource Source => PostParserSource.SoulPlus;

    public Task BuyAsync(string lockUrl, CancellationToken ct) => spClient.BuyLockedContent(lockUrl, ct);
}

/// <summary>
/// Which locks may be paid for without asking. A lock with no stated price is never one of them:
/// an unknown price used to compare as zero and buy itself.
/// </summary>
public static class SharedContentLockExtensions
{
    public static IEnumerable<SharedContentLock> Unbought(this IEnumerable<SharedContentLock> locks) =>
        locks.Where(l => !l.IsBought && !string.IsNullOrEmpty(l.Url));

    public static bool IsWithin(this SharedContentLock lockedContent, decimal limit) =>
        lockedContent.Price is { } price && price <= limit;
}
