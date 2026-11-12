using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Content = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

public sealed record ConfirmPostParserContentReadinessInput
{
    public int Revision { get; init; }
    public int? WorkflowRunId { get; init; }
    [Required, StringLength(64, MinimumLength = 64)] public string ExpectedContentFingerprint { get; init; } = "";
    public int ExpectedDecisionVersion { get; init; }
    [Required, MinLength(1), MaxLength(256)] public List<string> NotRequiredBlockIds { get; init; } = [];
    // Optional legacy client note. Confirmation authority comes from the explicit block selection and CAS.
    [MaxLength(1000)] public string? Reason { get; init; }
    public bool ContinueParsing { get; init; }
}

public sealed record RevokePostParserContentReadinessInput
{
    public int Revision { get; init; }
    public int? WorkflowRunId { get; init; }
    [Required, StringLength(64, MinimumLength = 64)] public string ExpectedContentFingerprint { get; init; } = "";
    public int ExpectedDecisionVersion { get; init; }
    [Required, MaxLength(64)] public string DecisionId { get; init; } = "";
}

public sealed record RefreshPostParserContentReadinessInput
{
    public int Revision { get; init; }
    public int? WorkflowRunId { get; init; }
    [Required, StringLength(64, MinimumLength = 64)] public string ExpectedContentFingerprint { get; init; } = "";
    public int ExpectedDecisionVersion { get; init; }
}

public sealed record PostParserContentReadinessEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int Version { get; init; }
    public string Action { get; init; } = "confirm";
    public string? DecisionId { get; init; }
    public string ContentFingerprint { get; init; } = "";
    public int SourceRevision { get; init; }
    public int? WorkflowRunId { get; init; }
    public List<string> BlockIds { get; init; } = [];
    public string Reason { get; init; } = "";
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public sealed record PostParserContentReadinessView
{
    public int TaskId { get; init; }
    public int Revision { get; init; }
    public int? WorkflowRunId { get; init; }
    public string? Source { get; init; }
    public Content? ContentSnapshot { get; init; }
    public string? ContentFingerprint { get; init; }
    public int DecisionVersion { get; init; }
    public string Status { get; init; } = "unknown";
    public string? Basis { get; init; }
    public PostContentReadinessAssessment? Assessment { get; init; }
    public string? ActiveDecisionId { get; init; }
    public bool HasStaleDecision { get; init; }
    public bool CanConfirm { get; init; }
    public bool CanContinue { get; init; }
    public bool CanRevoke { get; init; }
    public bool CanRefresh { get; init; }
    public bool ParsedResultMayBeStale { get; init; }
    public List<PostParserContentReadinessEvent> History { get; init; } = [];
    public List<PostContentEvidenceEquivalenceProof> EvidenceEquivalences { get; init; } = [];
}

/// <summary>Task-owned audit, independent from parsed results. Only the server creates manual authority.</summary>
public sealed record PostParserContentReadinessState
{
    public List<PostParserContentReadinessEvent> History { get; init; } = [];
    public List<PostParserContentReviewReceipt> Reviews { get; init; } = [];
    public List<PostContentEvidenceEquivalenceProof> EvidenceEquivalences { get; init; } = [];
    public PostParserReadinessProjection? Projection { get; init; }
    // A source-only refresh never rewrites historical extraction output. Only a newly
    // completed extraction can make that output authoritative for future consumers again.
    public bool ParsedResultMayBeStale { get; init; }
    public int Version => History.Count == 0 ? 0 : History.Max(e => e.Version);

    public static PostParserContentReadinessState Read(string? json)
    {
        try
        {
            var state = string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<PostParserContentReadinessState>(json, WorkflowJson.Options) ?? new();
            if (state.History == null || state.History.Any(e => e == null || e.BlockIds == null) || state.Reviews == null) return new();
            // Malformed aliases cannot grant authority. Preserve the original history/payment journal.
            return state.EvidenceEquivalences == null || state.EvidenceEquivalences.Count > 1000 ||
                state.EvidenceEquivalences.Any(p => p == null || string.IsNullOrWhiteSpace(p.PolicyId) || p.PolicyId.Length > 128 ||
                    !PostContentEvidenceEquivalence.Digest(p.BeforeFingerprint) || !PostContentEvidenceEquivalence.Digest(p.AfterFingerprint) ||
                    !PostContentEvidenceEquivalence.Digest(p.CanonicalFingerprint)) ? state with {EvidenceEquivalences = []} : state;
        }
        catch (JsonException) { return new(); }
    }

    public PostParserContentReadinessEvent? Active(Content content)
    {
        var fingerprint = PostContentReadinessPolicy.Fingerprint(content);
        var last = History.LastOrDefault();
        // The head is authoritative: revoking or confirming different evidence never revives an older decision.
        return last is {Action: "confirm"} && MatchesFingerprint(last.ContentFingerprint, fingerprint) ? last : null;
    }

    public bool MatchesFingerprint(string? original, string current)
    {
        if (!PostContentEvidenceEquivalence.Digest(original) || !PostContentEvidenceEquivalence.Digest(current)) return false;
        var reached = new HashSet<string>(StringComparer.Ordinal) {original!};
        if (reached.Contains(current)) return true;
        // Only persisted server proofs participate in projections; no source parsing or writes on GET.
        for (var i = 0; i < EvidenceEquivalences.Count; i++)
        {
            var changed = false;
            foreach (var proof in EvidenceEquivalences)
                if (reached.Contains(proof.BeforeFingerprint)) changed |= reached.Add(proof.AfterFingerprint);
            if (reached.Contains(current)) return true;
            if (!changed) break;
        }
        return false;
    }

    public PostContentReadinessAssessment? RebindAssessment(Content content, PostContentReadinessAssessment? proof) =>
        proof != null && MatchesFingerprint(proof.Fingerprint, PostContentReadinessPolicy.Fingerprint(content))
            ? proof with {Fingerprint = PostContentReadinessPolicy.Fingerprint(content)} : null;

    public PostParserContentReadinessState RecordEquivalent(Content previous, Content fresh,
        IEnumerable<IPostContentEvidenceCanonicalizer> canonicalizers, out bool equivalent)
    {
        equivalent = PostContentReadinessPolicy.Fingerprint(previous) == PostContentReadinessPolicy.Fingerprint(fresh);
        if (equivalent) return this;
        equivalent = PostContentEvidenceEquivalence.TryProve(previous, fresh, canonicalizers, out var proof);
        if (!equivalent || EvidenceEquivalences.Any(p => p.BeforeFingerprint == proof!.BeforeFingerprint && p.AfterFingerprint == proof.AfterFingerprint)) return this;
        if (EvidenceEquivalences.Count >= 1000) { equivalent = false; return this; }
        return this with {EvidenceEquivalences = EvidenceEquivalences.Append(proof!).ToList()};
    }

    public Content Apply(Content content)
    {
        var active = Active(content);
        var proof = active == null ? content.Readiness?.Basis == "analysis" ? RebindAssessment(content, content.Readiness) : null : new PostContentReadinessAssessment
        {
            Basis = "manual", PolicyVersion = PostContentReadinessPolicy.Version,
            Fingerprint = PostContentReadinessPolicy.Fingerprint(content),
            Decisions = active.BlockIds.Select(id => new PostContentBlockDecision
                {BlockId = id, Relevance = "optional", Reason = active.Reason}).ToList()
        };
        return PostContentReadinessPolicy.Apply(content, proof);
    }

    public PostParserContentReadinessState WithProjection(Content content) => this with
    {
        Projection = content.Readiness == null ? null : new()
        {
            Fingerprint = content.Readiness.Fingerprint,
            BlockingBlockIds = PostContentReadinessPolicy.BlockingLocks(content).Select(l => l.BlockId ?? "").ToList()
        }
    };

    public static bool HasStableBlockIds(Content content)
    {
        var remaining = content.Locks.Where(l => !l.IsBought).ToList();
        return remaining.Count is > 0 and <= 256 && remaining.All(l => !string.IsNullOrWhiteSpace(l.BlockId)) &&
               remaining.Select(l => l.BlockId).Distinct(StringComparer.Ordinal).Count() == remaining.Count;
    }
}

/// <summary>Small, trusted projection written atomically with the content snapshot, never inferred by a client.</summary>
public sealed record PostParserReadinessProjection
{
    public string Fingerprint { get; init; } = "";
    public List<string> BlockingBlockIds { get; init; } = [];
}
