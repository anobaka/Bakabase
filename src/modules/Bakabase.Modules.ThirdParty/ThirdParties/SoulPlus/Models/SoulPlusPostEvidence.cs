namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models;

/// <summary>Source-owned DOM evidence. Kept independent of the post-processing module.</summary>
public sealed record SoulPlusPostEvidence(string SourceIdentity, string ContentFingerprint,
    string OutsideSlotsDigest, List<SoulPlusPostEvidenceSlot> Slots);

public sealed record SoulPlusPostEvidenceSlot(string BlockId, string OwnerId, string Floor,
    string CanonicalTargetId, string? PurchaseTargetId, bool IsReadable, decimal? Price, string MetadataDigest, string ContentDigest);
