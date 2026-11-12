using System;
using System.Linq;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;
using Content = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

/// <summary>Pure proof over saved source; the caller retains the original approval/history hashes.</summary>
public sealed class SoulPlusPostEvidenceCanonicalizer : IPostContentEvidenceCanonicalizer
{
    public string PolicyId => SoulPlusPostParser.EvidencePolicyId;

    public bool TryCanonicalSourceFingerprint(Content content, out string fingerprint)
    {
        fingerprint = "";
        if (content.SourceHint != "SoulPlus" || content.SourceUrl == null || content.Scope != "firstPage" ||
            content.MainHtml == null || content.Comments == null || content.CommentHtmlList == null || content.Locks == null ||
            content.Comments.Any(c => c == null || c.Html == null) || content.Locks.Any(l => l == null) ||
            content.Locks.Count > 256 || content.CommentHtmlList.Count > 0 &&
            !content.CommentHtmlList.SequenceEqual(content.Comments.Select(c => c.Html), StringComparer.Ordinal)) return false;
        var comments = content.Comments.Select(c => new SoulPlusPostComment
            {Id = c.Id, Floor = c.Floor, Author = c.Author, PostedAt = c.PostedAt, Html = c.Html}).ToList();
        if (!SoulPlusPostParser.TryCanonicalSnapshotFingerprint(content.SourceUrl, content.Title, content.MainHtml,
                comments, content.SourceEvidenceFingerprint, out var canonical, out var parsed) || parsed.Count != content.Locks.Count) return false;
        for (var i = 0; i < parsed.Count; i++)
        {
            var actual = parsed[i]; var saved = content.Locks[i];
            if (actual.Url != saved.Url || actual.Price != saved.Price || actual.IsBought != saved.IsBought ||
                actual.BlockId != saved.BlockId || actual.Floor != saved.Floor ||
                actual.PurchaseTargetId != saved.PurchaseTargetId || actual.ContextText != saved.ContextText ||
                saved.CurrencyCode != "SP") return false;
        }
        fingerprint = canonical;
        return true;
    }
}
