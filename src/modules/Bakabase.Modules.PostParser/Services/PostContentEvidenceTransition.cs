using System.Globalization;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Validates reader evidence, never grants purchase authority or re-signs an old whole-post decision.</summary>
public static class PostContentEvidenceTransition
{
    public static bool TryValidateAuthorizedUnlock(PostContent previous, PostContent fresh,
        IReadOnlyCollection<string> approvedTargetIds, out PostContentUnlockTransition? transition)
    {
        transition = null;
        var before = previous.ReaderEvidence;
        var after = fresh.ReaderEvidence;
        if (!Valid(previous, before) || !Valid(fresh, after) || approvedTargetIds.Count is 0 or > 256 ||
            approvedTargetIds.Any(string.IsNullOrWhiteSpace) ||
            before!.Version != after!.Version || before.ReaderId != after.ReaderId ||
            before.SourceIdentity != after.SourceIdentity || before.OutsideSlotsDigest != after.OutsideSlotsDigest ||
            previous.SourceHint != fresh.SourceHint || previous.Scope != fresh.Scope ||
            before.Slots.Count != after.Slots.Count) return false;

        var approved = approvedTargetIds.ToHashSet(StringComparer.Ordinal);
        var next = after.Slots.ToDictionary(s => s.BlockId, StringComparer.Ordinal);
        var unchanged = new List<string>();
        var unlocked = new List<string>();
        var changedTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var old in before.Slots)
        {
            if (!next.TryGetValue(old.BlockId, out var current) || old.OwnerId != current.OwnerId ||
                old.Floor != current.Floor || old.CanonicalTargetId != current.CanonicalTargetId ||
                old.MetadataDigest != current.MetadataDigest || old.CurrencyCode != current.CurrencyCode) return false;
            if (old.IsReadable == current.IsReadable)
            {
                if (old.ContentDigest != current.ContentDigest || old.Price != current.Price) return false;
                unchanged.Add(old.BlockId);
                continue;
            }
            // A purchase may remove the site's price caption, but a visible new price must agree.
            // Missing endpoint/price alone never proves an unlock: the reader must have recognized
            // the readable marker in the same DOM-backed slot and owner, with unchanged outside HTML.
            if (old.IsReadable || !current.IsReadable || !approved.Contains(old.CanonicalTargetId) ||
                old.Price is null or < 0 || current.Price is { } price && price != old.Price) return false;
            unlocked.Add(old.BlockId);
            changedTargets.Add(old.CanonicalTargetId);
        }
        // Partial unlocks of a shared payment target are not authoritative purchase receipts.
        // Each approved target must actually change, and every one of its old slots must unlock.
        if (!changedTargets.SetEquals(approved) || before.Slots.Any(s => approved.Contains(s.CanonicalTargetId) &&
                (s.IsReadable || !unlocked.Contains(s.BlockId)))) return false;
        transition = new(unchanged, unlocked, changedTargets.Order(StringComparer.Ordinal).ToList());
        return true;
    }

    private static bool Valid(PostContent content, PostContentReaderEvidence? evidence)
    {
        if (evidence == null || evidence.Version != "post-content-slots-v1" ||
            string.IsNullOrWhiteSpace(evidence.ReaderId) || string.IsNullOrWhiteSpace(evidence.SourceIdentity) ||
            !Digest(evidence.ContentFingerprint) || evidence.ContentFingerprint != content.SourceEvidenceFingerprint ||
            !Digest(evidence.OutsideSlotsDigest) || evidence.Slots is not {Count: > 0 and <= 256} ||
            content.Locks.Count != evidence.Slots.Count ||
            evidence.Slots.Any(s => s == null || string.IsNullOrWhiteSpace(s.BlockId) ||
                string.IsNullOrWhiteSpace(s.OwnerId) || string.IsNullOrWhiteSpace(s.CanonicalTargetId) ||
                !int.TryParse(s.Floor, NumberStyles.None, CultureInfo.InvariantCulture, out var floor) || floor < 0 ||
                !Digest(s.MetadataDigest) || !Digest(s.ContentDigest)) ||
            evidence.Slots.Select(s => s.BlockId).Distinct(StringComparer.Ordinal).Count() != evidence.Slots.Count ||
            content.Locks.Any(l => l.BlockId == null) ||
            content.Locks.Select(l => l.BlockId).Distinct(StringComparer.Ordinal).Count() != content.Locks.Count) return false;
        var locks = content.Locks.ToDictionary(l => l.BlockId!, StringComparer.Ordinal);
        return evidence.Slots.All(s => locks.TryGetValue(s.BlockId, out var locked) &&
            locked.IsBought == s.IsReadable && locked.PurchaseTargetId == s.PurchaseTargetId &&
            (s.IsReadable || locked.Price == s.Price) && locked.CurrencyCode == s.CurrencyCode &&
            int.TryParse(locked.Floor, NumberStyles.None, CultureInfo.InvariantCulture, out var floor) &&
            floor.ToString(CultureInfo.InvariantCulture) == s.Floor);
    }

    private static bool Digest(string? value) => value is {Length: 64} && value.All(Uri.IsHexDigit);
}

public sealed record PostContentUnlockTransition(IReadOnlyList<string> UnchangedBlockIds,
    IReadOnlyList<string> UnlockedBlockIds, IReadOnlyList<string> UnlockedTargetIds);
