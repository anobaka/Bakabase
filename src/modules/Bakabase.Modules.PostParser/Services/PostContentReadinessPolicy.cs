using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>One completeness policy for extraction, workflows and presentation. Payment prices remain independent.</summary>
public static class PostContentReadinessPolicy
{
    public const string Version = "content-readiness-v1";
    private static readonly HashSet<string> Purposes = ["purchaseProof", "donation", "unrelated", "supplement", "downloadCredentials", "processingInstructions", "unknown"];

    public static string Fingerprint(PostContent content)
    {
        // Account balance, capture time and changing purchase nonces are not editorial evidence.
        // Readable source, all block descriptions and prices ARE evidence, including optional blocks.
        var value = JsonSerializer.Serialize(new
        {
            content.SourceHint, content.SourceUrl, content.Scope, Title = Normalize(content.Title),
            SourceEvidence = content.SourceEvidenceFingerprint,
            Body = content.SourceEvidenceFingerprint == null ? content.MainHtml : null,
            Comments = content.SourceEvidenceFingerprint != null ? null : content.Comments.Count > 0
                ? content.Comments.Select(c => new {c.Id, c.Floor, Text = c.Html})
                : content.CommentHtmlList.Select((c, i) => new {Id = (string?)null, Floor = (string?)i.ToString(), Text = c}),
            Locks = content.Locks.Select(l => new {l.BlockId, l.Floor, l.Price, l.IsBought, l.CurrencyCode,
                Context = Normalize(l.ContextText ?? ""), Target = l.PurchaseTargetId ?? l.Url})
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static bool IsCurrent(PostContent content, PostContentReadinessAssessment? assessment) =>
        assessment != null && assessment.PolicyVersion == Version && assessment.Fingerprint == Fingerprint(content) &&
        assessment.Basis is "analysis" or "manual" && assessment.Decisions is {Count: <= 256};

    public static PostContent Apply(PostContent content, PostContentReadinessAssessment? assessment) =>
        content with {Readiness = IsCurrent(content, assessment) ? assessment : null};

    public static IReadOnlyList<PostContentLock> BlockingLocks(PostContent content)
    {
        var remaining = content.Locks.Where(l => !l.IsBought).ToList();
        var assessment = content.Readiness;
        if (!IsCurrent(content, assessment)) return remaining;
        var identities = remaining.GroupBy(l => l.BlockId ?? "").ToDictionary(g => g.Key, g => g.Count());
        return remaining.Where(locked =>
        {
            if (string.IsNullOrEmpty(locked.BlockId) || identities[locked.BlockId] != 1) return true;
            var matches = assessment!.Decisions.Where(d => d.BlockId == locked.BlockId).ToList();
            return matches.Count != 1 || !CanIgnore(locked, matches[0], assessment.Basis);
        }).ToList();
    }

    public static bool HasBlockingLocks(PostContent content) => BlockingLocks(content).Count > 0;

    private static bool CanIgnore(PostContentLock locked, PostContentBlockDecision decision, string basis)
    {
        if (decision.Relevance != "optional") return false;
        if (basis == "manual") return true; // Only the task service creates this from its persisted, CAS-checked approval.
        if (decision.Purpose is not ("purchaseProof" or "donation" or "unrelated" or "supplement")) return false;
        var context = Normalize(locked.ContextText ?? "");
        return context.Length > 0 && decision.Evidence is {Count: > 0 and <= 8} &&
               decision.Evidence.All(e => !string.IsNullOrWhiteSpace(e) && e.Length <= 500 &&
                   context.Contains(Normalize(e), StringComparison.Ordinal));
    }

    /// <summary>Never trust model-supplied fingerprints, manual authority or block identities.</summary>
    public static PostContentReadinessAssessment FromAnalysis(PostContent content, IEnumerable<PostContentBlockDecision>? proposed)
    {
        var candidates = (proposed ?? []).Take(257).ToList();
        var decisions = new List<PostContentBlockDecision>();
        foreach (var locked in content.Locks.Where(l => !l.IsBought && !string.IsNullOrWhiteSpace(l.BlockId)))
        {
            var matching = candidates.Where(d => d != null && d.BlockId == locked.BlockId).ToList();
            var candidate = matching.Count == 1 ? matching[0] : null;
            if (candidate == null || candidate.Relevance is not ("required" or "optional" or "unknown") ||
                !Purposes.Contains(candidate.Purpose) || candidate.Evidence == null || candidate.Evidence.Count > 8 ||
                candidate.Evidence.Any(e => e == null || e.Length > 500) || candidate.Reason?.Length > 1000 ||
                candidate.Relevance == "optional" && !CanIgnore(locked, candidate, "analysis"))
                candidate = new() {BlockId = locked.BlockId!, Reason = "No supported evidence resolves this restricted block."};
            decisions.Add(candidate);
        }
        return new() {Fingerprint = Fingerprint(content), PolicyVersion = Version, Basis = "analysis", Decisions = decisions};
    }

    /// <summary>Reuse a decision only for identical evidence. Newly readable content may change a block's purpose.</summary>
    public static PostContent Reconcile(PostContent fresh, PostContent previous,
        IEnumerable<IPostContentEvidenceCanonicalizer>? canonicalizers = null)
    {
        if (IsCurrent(fresh, previous.Readiness)) return Apply(fresh, previous.Readiness);
        if (canonicalizers != null && IsCurrent(previous, previous.Readiness) &&
            PostContentEvidenceEquivalence.TryProve(previous, fresh, canonicalizers, out _))
            return Apply(fresh, previous.Readiness! with {Fingerprint = Fingerprint(fresh)});
        return fresh with {Readiness = null};
    }

    public static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();

}
