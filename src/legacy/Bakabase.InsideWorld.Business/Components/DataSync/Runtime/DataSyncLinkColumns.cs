using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// Reads and writes the JSON columns of <see cref="DataSyncLinkDbModel"/> (§4.1) with <see cref="DataSyncJson.Options"/>,
/// and the link facts derived from them. Reads are tolerant: a column that does not parse reads as its default,
/// because a link row must never become unusable over one bad column.
/// </summary>
public static class DataSyncLinkColumns
{
    /// <summary>The link's kinds in apply order (<see cref="DataSyncKindIds.All"/>); both kinds when the column is empty.</summary>
    public static IReadOnlyList<string> GetKinds(this DataSyncLinkDbModel link)
    {
        var kinds = Read<List<string>>(link.KindsJson);
        if (kinds is not { Count: > 0 }) return DataSyncKindIds.All;
        return DataSyncKindIds.All.Where(kinds.Contains).ToList();
    }

    public static void SetKinds(this DataSyncLinkDbModel link, IEnumerable<string> kinds) =>
        link.KindsJson = Write(DataSyncKindIds.All.Where(kinds.Contains).ToList());

    /// <summary>Kind → the source Seq fully evaluated and committed (§7.5.5).</summary>
    public static Dictionary<string, long> GetCursors(this DataSyncLinkDbModel link) =>
        Read<Dictionary<string, long>>(link.CursorsJson) is { } cursors
            ? new Dictionary<string, long>(cursors, StringComparer.Ordinal)
            : new Dictionary<string, long>(StringComparer.Ordinal);

    public static void SetCursors(this DataSyncLinkDbModel link, IReadOnlyDictionary<string, long> cursors) =>
        link.CursorsJson = Write(cursors.OrderBy(c => c.Key, StringComparer.Ordinal)
            .ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal));

    /// <summary>Kinds whose first contact on this link has completed.</summary>
    public static IReadOnlyList<string> GetFirstContactKinds(this DataSyncLinkDbModel link) =>
        Read<List<string>>(link.FirstContactKindsJson) ?? [];

    public static void SetFirstContactKinds(this DataSyncLinkDbModel link, IEnumerable<string> kinds) =>
        link.FirstContactKindsJson = Write(DataSyncKindIds.All.Where(kinds.Contains).ToList());

    /// <summary>
    /// The link's kinds whose first contact has not completed yet: kinds added to the link after it (§8.1), or every
    /// kind before it. A link whose first contact completed without a kind list reads as complete for every kind, so
    /// it can never be sent back to a review.
    /// </summary>
    public static IReadOnlyList<string> GetKindsAwaitingFirstContact(this DataSyncLinkDbModel link)
    {
        if (link.FirstContactKindsJson is null && link.FirstContactCompletedAtUtc is not null) return [];
        var done = link.GetFirstContactKinds();
        return link.GetKinds().Where(k => !done.Contains(k)).ToList();
    }

    public static DataSyncFeedCounterpart? GetCounterpart(this DataSyncLinkDbModel link) =>
        Read<DataSyncFeedCounterpart>(link.CounterpartJson);

    public static void SetCounterpart(this DataSyncLinkDbModel link, DataSyncFeedCounterpart? counterpart) =>
        link.CounterpartJson = counterpart is null ? null : Write(counterpart);

    public static DataSyncSourceAttention? GetPeerAttention(this DataSyncLinkDbModel link) =>
        Read<DataSyncSourceAttention>(link.PeerAttentionJson);

    public static void SetPeerAttention(this DataSyncLinkDbModel link, DataSyncSourceAttention? attention) =>
        link.PeerAttentionJson = attention is null ? null : Write(attention);

    /// <summary>The once flags the next apply of this link consumes (§8.7).</summary>
    public static DataSyncMergeFlags GetOnceFlags(this DataSyncLinkDbModel link) =>
        Read<DataSyncMergeFlags>(link.OnceFlagsJson) ?? DataSyncMergeFlags.None;

    public static void SetOnceFlags(this DataSyncLinkDbModel link, DataSyncMergeFlags flags) =>
        link.OnceFlagsJson = flags == DataSyncMergeFlags.None ? null : Write(flags);

    /// <summary>
    /// The once flags that act on pending records alone, so an apply without a new pull may consume them
    /// (<c>SkipLargeChange</c>: "Apply all" re-merges the waiting records at once, §8.7 B5). The deletion flags act
    /// on the deletions of the pull that tripped B2/B3, which the next fetch brings again, so they ride with it.
    /// </summary>
    public static DataSyncMergeFlags PullIndependent(this DataSyncMergeFlags flags) =>
        flags.SkipLargeChange ? DataSyncMergeFlags.None with { SkipLargeChange = true } : DataSyncMergeFlags.None;

    /// <summary>The flags left after an apply consumed <paramref name="consumed"/>.</summary>
    public static DataSyncMergeFlags Without(this DataSyncMergeFlags flags, DataSyncMergeFlags consumed) =>
        new(DeletionsAsItems: flags.DeletionsAsItems && !consumed.DeletionsAsItems,
            SkipDeletionBreaker: flags.SkipDeletionBreaker && !consumed.SkipDeletionBreaker,
            SkipLargeChange: flags.SkipLargeChange && !consumed.SkipLargeChange,
            ChildDeletions: consumed.ChildDeletions == flags.ChildDeletions
                ? DataSyncMergeFlags.None.ChildDeletions
                : flags.ChildDeletions);

    /// <summary>
    /// The mode the merge uses (§8.1): <see cref="DataSyncLinkMode.TwoWay"/> when this device follows the peer and
    /// the peer's counterpart says it follows this device too (mutual Follow is two-way, engineering B8).
    /// </summary>
    public static DataSyncLinkMode GetEffectiveMode(this DataSyncLinkDbModel link) =>
        link.Mode == DataSyncLinkMode.Follow && link.GetCounterpart()?.Mode == "follow"
            ? DataSyncLinkMode.TwoWay
            : link.Mode;

    /// <summary>The <c>mode</c> this device declares in head and manifest queries (§7.5): a copy once reads like Follow.</summary>
    public static string GetDeclaredMode(this DataSyncLinkDbModel link) =>
        link.GetEffectiveMode() == DataSyncLinkMode.TwoWay ? "twoWay" : "follow";

    /// <summary>
    /// The state a link returns to once nothing holds it (a pause resumed, access back, a peer answering again): Active
    /// after its first contact (Stopped for a copy once), else the side of the first contact it is on (§8.1, §8.3).
    /// </summary>
    public static DataSyncLinkState GetResumeState(this DataSyncLinkDbModel link)
    {
        if (link.FirstContactCompletedAtUtc is not null)
            return link.Mode == DataSyncLinkMode.Off ? DataSyncLinkState.Stopped : DataSyncLinkState.Active;
        return link.Initiator == DataSyncLinkInitiator.ThisDevice
            ? DataSyncLinkState.AwaitingReview
            : DataSyncLinkState.WaitingForPeerReview;
    }

    /// <summary>The states a failed peer call sets (§8.1): they end when the peer answers again.</summary>
    public static bool IsPeerErrorState(this DataSyncLinkState state) =>
        state is DataSyncLinkState.AccessRevoked or DataSyncLinkState.PeerSharingOff
            or DataSyncLinkState.PeerRemoteAccessOff or DataSyncLinkState.PeerTooOld or DataSyncLinkState.ThisTooOld;

    /// <summary>
    /// Whether the fetch cycle looks at this link: every state except Paused and Stopped. A link whose mode is Off is
    /// a copy once while it waits for access or its review (§8.1).
    /// </summary>
    public static bool IsFetchable(this DataSyncLinkDbModel link)
    {
        if (link.State is DataSyncLinkState.Paused or DataSyncLinkState.Stopped) return false;
        if (link.Mode != DataSyncLinkMode.Off) return true;
        return link.State is DataSyncLinkState.AwaitingAccess or DataSyncLinkState.AwaitingReview ||
               link.State.IsPeerErrorState() && link.FirstContactCompletedAtUtc is null;
    }

    /// <summary>The <c>state</c> this device declares to the peer (§7.5.6): <c>ok|awaitingReview|paused:{reason}|needsYou:{n}</c>.</summary>
    public static string GetDeclaredState(this DataSyncLinkDbModel link, int openItems) => link.State switch
    {
        DataSyncLinkState.AwaitingReview or DataSyncLinkState.WaitingForPeerReview => "awaitingReview",
        DataSyncLinkState.Paused => "paused:" + JsonNamingPolicy.CamelCase.ConvertName(
            (link.PausedReason ?? DataSyncPauseReason.ByUser).ToString()),
        _ when openItems > 0 => "needsYou:" + openItems.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "ok",
    };

    public static string ToCode(this DataSyncPeerErrorCode code) => code.ToString();

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, DataSyncJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string Write<T>(T value) => JsonSerializer.Serialize(value, DataSyncJson.Options);
}
