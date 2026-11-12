using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>A reader must validate the saved digest against the complete snapshot before canonicalizing it.</summary>
public interface IPostContentEvidenceCanonicalizer
{
    string PolicyId { get; }
    bool TryCanonicalSourceFingerprint(PostContent content, out string fingerprint);
}

/// <summary>Proof of source-owned volatile changes. The original whole-post digests remain immutable audit identities.</summary>
public sealed record PostContentEvidenceEquivalenceProof(string PolicyId, string BeforeFingerprint,
    string AfterFingerprint, string CanonicalFingerprint, DateTime CreatedAt);

public static class PostContentEvidenceEquivalence
{
    public static bool TryCanonicalWholeFingerprint(PostContent content,
        IEnumerable<IPostContentEvidenceCanonicalizer> canonicalizers, out string policyId, out string fingerprint)
    {
        policyId = ""; fingerprint = "";
        foreach (var policy in canonicalizers)
        {
            if (string.IsNullOrWhiteSpace(policy.PolicyId) || policy.PolicyId.Length > 128 ||
                !policy.TryCanonicalSourceFingerprint(content, out var source) || !Digest(source)) continue;
            policyId = policy.PolicyId;
            fingerprint = PostContentReadinessPolicy.Fingerprint(content with {SourceEvidenceFingerprint = source});
            return true;
        }
        return false;
    }

    public static bool TryProve(PostContent previous, PostContent fresh,
        IEnumerable<IPostContentEvidenceCanonicalizer> canonicalizers, out PostContentEvidenceEquivalenceProof? proof)
    {
        proof = null;
        foreach (var policy in canonicalizers)
        {
            if (string.IsNullOrWhiteSpace(policy.PolicyId) || policy.PolicyId.Length > 128 ||
                !policy.TryCanonicalSourceFingerprint(previous, out var oldSource) || !Digest(oldSource) ||
                !policy.TryCanonicalSourceFingerprint(fresh, out var newSource) || !Digest(newSource)) continue;
            // Source evidence alone is insufficient: prices, owners, payment targets, contexts,
            // block identities and readable states still participate in the ordinary whole hash.
            var before = PostContentReadinessPolicy.Fingerprint(previous with {SourceEvidenceFingerprint = oldSource});
            var after = PostContentReadinessPolicy.Fingerprint(fresh with {SourceEvidenceFingerprint = newSource});
            if (before != after) continue;
            proof = new(policy.PolicyId, PostContentReadinessPolicy.Fingerprint(previous),
                PostContentReadinessPolicy.Fingerprint(fresh), before, DateTime.UtcNow);
            return true;
        }
        return false;
    }

    public static bool Digest(string? value) => value is {Length: 64} && value.All(Uri.IsHexDigit);
}
