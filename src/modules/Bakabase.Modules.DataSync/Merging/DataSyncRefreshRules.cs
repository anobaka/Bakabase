using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// The pure core of Refresh (§6.1): for one live row whose local content was read, what changes. Refresh [C] and
/// the simulator call it the same way, so a revision is issued exactly when the comparison form changes — never
/// for a local-only difference, an unsynced row or an unreadable one — and a revert of a recent apply is held for a
/// person instead of published (§6.5).
/// </summary>
public static class DataSyncRefreshRules
{
    /// <summary>
    /// Evaluates one row. <paramref name="lostUpdate"/> is asked only when a revision would otherwise be issued, and
    /// returns the applied changes the content undoes (<see cref="DataSyncLostUpdateGuard.UndoneChanges"/>, with the
    /// row's most recent covered apply inside the window), or null / empty when there is nothing to suspect.
    /// </summary>
    /// <param name="localContent">The adapter's canonical local content (<c>LocalEntity.Content</c>).</param>
    /// <param name="movedOrderKey">The new order key <c>DetectMoves</c> gave the row, if it moved (§3.7).</param>
    public static DataSyncRefreshDecision Evaluate(IDataSyncKindCodec codec, DataSyncRefreshRow row,
        JsonObject localContent, bool unreadable, string? movedOrderKey,
        Func<object, DataSyncEntityChangeList?>? lostUpdate = null)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(localContent);
        var localHash = ContentHash.Of(localContent);

        // Never a revision for an unsynced or unreadable row: only its local hash follows (no Seq).
        if (row.State != DataSyncEntitySyncState.Synced || unreadable)
            return new DataSyncRefreshDecision(DataSyncRefreshAction.HashesOnly, localHash, null, row.OrderKey, row.SharedHash, null);

        var typed = codec.ReadLocal(localContent);
        var orderKey = movedOrderKey ?? row.OrderKey;
        var publication = DataSyncPublication.Of(codec, typed, row.Overlay, row.ChildrenLocal, orderKey, row.Unknown);
        var shared = publication.SharedHash ?? HeldSharedHash(localHash);
        if (shared == row.SharedHash)
            return new DataSyncRefreshDecision(DataSyncRefreshAction.HashesOnly, localHash, publication, orderKey, shared, null);

        // Already held: a later local edit only refreshes the open item (§6.5).
        if (row.PublishHeld)
            return new DataSyncRefreshDecision(DataSyncRefreshAction.RefreshHeldItem, localHash, publication, row.OrderKey,
                row.SharedHash, null);

        if (lostUpdate?.Invoke(typed) is { IsEmpty: false } undone)
        {
            return new DataSyncRefreshDecision(DataSyncRefreshAction.HoldForLostUpdate, localHash, publication, row.OrderKey,
                row.SharedHash, undone);
        }

        return new DataSyncRefreshDecision(DataSyncRefreshAction.LocalEdit, localHash, publication, orderKey, shared, null);
    }

    /// <summary>
    /// The vector of a row's local revision (<c>LocalEdit</c>, a local deletion's <c>LocalDelete</c>): through
    /// <see cref="DataSyncRevisionRules"/>, never by hand.
    /// </summary>
    public static DataSyncVersionVector LocalRevision(DataSyncVersionVector current, DataSyncActorId self,
        Func<long> nextCounter, bool deletion = false) =>
        DataSyncRevisionRules.Next(deletion ? DataSyncRevisionKind.LocalDelete : DataSyncRevisionKind.LocalEdit, current,
            null, false, false, self, nextCounter);

    /// <summary>
    /// The <c>SharedHash</c> a row keeps while its publication is held (§3.5 step 4: too large, invalid): derived
    /// from the local hash, so every local change of a held entity is still a revision and readers receive the
    /// <c>HeldAtSource</c> record's new version.
    /// </summary>
    public static string HeldSharedHash(string localHash) => "held:" + localHash;

    /// <summary>§6.3: a tombstone is served only when the row was <c>Synced</c> when it was deleted.</summary>
    public static bool ServesTombstone(DataSyncEntitySyncState stateAtDeletion) =>
        stateAtDeletion == DataSyncEntitySyncState.Synced;
}

/// <summary>A live row as Refresh compares it (the stored side of §6.1).</summary>
/// <param name="SharedHash">The stored comparison-form hash; null for a row Refresh has not hashed yet.</param>
public sealed record DataSyncRefreshRow(DataSyncEntitySyncState State, string? SharedHash, string? OrderKey,
    DataSyncOverlay Overlay, bool ChildrenLocal, bool PublishHeld, JsonObject? Unknown);

/// <summary>What Refresh does with one row (<see cref="DataSyncRefreshRules.Evaluate"/>).</summary>
/// <param name="Publication">What the row publishes now (null for an unsynced or unreadable row).</param>
/// <param name="OrderKey">The order key to store (the moved one only with a revision).</param>
/// <param name="SharedHash">The comparison-form hash to store.</param>
/// <param name="Undone">For <see cref="DataSyncRefreshAction.HoldForLostUpdate"/>: the changes the content undid.</param>
public sealed record DataSyncRefreshDecision(DataSyncRefreshAction Action, string LocalHash, DataSyncPublication? Publication,
    string? OrderKey, string? SharedHash, DataSyncEntityChangeList? Undone);

/// <summary>What Refresh does with one row. Never on the wire or in an HTTP record.</summary>
public enum DataSyncRefreshAction
{
    /// <summary>Only the local hashes change: no revision, no Seq.</summary>
    HashesOnly = 1,

    /// <summary>A local revision: <c>LocalEdit</c> vector, Seq, this device as the last editor.</summary>
    LocalEdit = 2,

    /// <summary>The lost-update guard fired: no revision; <c>PublishHeld</c>, a Seq bump and a <c>SuspectedLostUpdate</c> item.</summary>
    HoldForLostUpdate = 3,

    /// <summary>Already held: no revision; the open <c>SuspectedLostUpdate</c> item's payload is refreshed.</summary>
    RefreshHeldItem = 4,
}
