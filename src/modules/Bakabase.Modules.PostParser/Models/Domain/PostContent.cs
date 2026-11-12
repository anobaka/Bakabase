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
    public string? CurrencyCode { get; init; }
    public string? CurrencyName { get; init; }
    /// <summary>Evidence about which unread blocks are needed, independent of whether they were purchased.</summary>
    public PostContentReadinessAssessment? Readiness { get; init; }
    /// <summary>Reader-owned digest of editorial evidence; excludes site session nonces and counters, never resource credentials.</summary>
    public string? SourceEvidenceFingerprint { get; init; }
    /// <summary>Reader-owned slot evidence for checking an authorized purchase transition; absent for unsupported readers and old snapshots.</summary>
    public PostContentReaderEvidence? ReaderEvidence { get; init; }

    /// <summary>Only freshly read, correctly encoded HTML may be compacted; restored snapshots keep their source.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public bool HtmlEncodingTrusted { get; init; }

    /// <summary>The selected platform reader, when the content has a known source.</summary>
    public string? SourceHint { get; init; }
}

public sealed record PostContentReaderEvidence
{
    public string Version { get; init; } = "post-content-slots-v1";
    public string ReaderId { get; init; } = "";
    public string SourceIdentity { get; init; } = "";
    public string ContentFingerprint { get; init; } = "";
    public string OutsideSlotsDigest { get; init; } = "";
    public List<PostContentEvidenceSlot> Slots { get; init; } = [];
}

/// <summary>DOM-backed payment slot. Metadata stays visible across unlocking; content is allowed to change only for an approved target.</summary>
public sealed record PostContentEvidenceSlot
{
    public string BlockId { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string Floor { get; init; } = "";
    public string CanonicalTargetId { get; init; } = "";
    public string? PurchaseTargetId { get; init; }
    public bool IsReadable { get; init; }
    public decimal? Price { get; init; }
    public string? CurrencyCode { get; init; }
    public string MetadataDigest { get; init; } = "";
    public string ContentDigest { get; init; } = "";
}

/// <summary>Describes restricted content; reading a post never purchases it.</summary>
public record PostContentLock(string? Url, decimal? Price, bool IsBought)
{
    public string? Id { get; init; }
    public string? Floor { get; init; }
    /// <summary>Identity of a semantic block, not a payment endpoint (several blocks may share one endpoint).</summary>
    public string? BlockId { get; init; }
    public string? PurchaseTargetId { get; init; }
    public string? ContextText { get; init; }
    public string? CurrencyCode { get; init; }
    public string? CurrencyName { get; init; }
    public string? PriceText => Price?.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
}

public record PostComment
{
    public string? Id { get; init; }
    public string? Floor { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset? PostedAt { get; init; }
    public string Html { get; init; } = "";
}
