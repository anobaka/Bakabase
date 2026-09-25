using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// What one local entity publishes and how it compares across devices (§3.4, §3.5), as Refresh computes it (§6.1)
/// and as an apply records it from the re-read entity (echo prevention, §6.4).
/// </summary>
/// <param name="LocalHash">v3.1's <c>ContentHash</c> of the local canonical content: a local change detector only.</param>
/// <param name="SharedHash">
/// <c>ContentHash</c> of the comparison form; <see cref="DataSyncEntityForms.HeldMarker"/> of the reason when the entity
/// is held at source; null when this device cannot read its own row (§3.3), which has no form.
/// </param>
/// <param name="Published">What a reader receives for it (held at source, or content with its hash).</param>
public sealed record DataSyncEntityForm(string LocalHash, string? SharedHash, DataSyncPublishedEntity Published)
{
    public bool IsHeld => Published.Held is not null;
}

/// <summary>
/// The forms of one entity, computed the same way everywhere: by Refresh for its own entities, by the apply runner
/// for the entity it re-read after a write, and for a peer record this device agreed to (§3.4: every device computes
/// <c>SharedHash</c> with its own codec and never reads it from the wire).
/// </summary>
/// <remarks>
/// Every form comes from the pure module's shared helpers (<see cref="DataSyncPublication"/>,
/// <see cref="DataSyncContentForms"/>), which the merger and the simulator use too: Refresh, the store, the apply
/// path and the merger compute one hash. This class only adds what the side row stores around them (the held marker,
/// the local hash, an unreadable row).
/// </remarks>
public static class DataSyncEntityForms
{
    /// <summary>The <c>SharedHash</c> stored for an entity that has no comparison form yet (a row this device cannot read).</summary>
    public const string NoSharedHash = "";

    private const string HeldPrefix = "held:";

    /// <summary>
    /// The <c>SharedHash</c> stored while an entity is held at source (§3.5 step 4). It never equals a content hash,
    /// so becoming publishable again is a change, and so is becoming held (the feed then serves HeldAtSource).
    /// </summary>
    public static string HeldMarker(DataSyncHeldReason reason) => HeldPrefix + reason;

    public static bool IsHeldMarker(string? sharedHash) =>
        sharedHash is not null && sharedHash.StartsWith(HeldPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The forms of a local entity (§3.5, §3.4): overlays and withheld children out, validated by the codec, the
    /// preserved unknown members back in. <paramref name="orderKey"/> is the one the entity publishes (§3.7).
    /// </summary>
    public static DataSyncEntityForm Evaluate(IDataSyncKindCodec codec, LocalEntity local, DataSyncOverlay overlay,
        bool childrenLocal, string? orderKey, JsonObject? unknown)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(overlay);
        var localHash = ContentHash.Of(local.Content);
        if (local.Unreadable)
        {
            return new DataSyncEntityForm(localHash, null,
                new DataSyncPublishedEntity(null, null, 0, DataSyncHeldReason.LocalUnreadable, "localUnreadable"));
        }

        var publication = DataSyncPublication.Of(codec, codec.ReadLocal(local.Content), overlay, childrenLocal, orderKey,
            unknown);
        return publication.Held is { } held
            ? new DataSyncEntityForm(localHash, HeldMarker(held),
                new DataSyncPublishedEntity(null, null, publication.ChildrenWithheld, held, publication.HeldDetail))
            : new DataSyncEntityForm(localHash, publication.SharedHash,
                new DataSyncPublishedEntity(publication.Content, publication.Hash, publication.ChildrenWithheld));
    }

    /// <summary>A record's content (§3.5 step 5): <see cref="DataSyncContentForms.PublishedContent"/>.</summary>
    public static JsonObject PublishedContent(IDataSyncKindCodec codec, object publishedContent, JsonObject? unknown) =>
        DataSyncContentForms.PublishedContent(codec, publishedContent, unknown);

    /// <summary>The comparison form (§3.4): <see cref="DataSyncContentForms.ComparisonForm"/>.</summary>
    public static JsonObject ComparisonForm(IDataSyncKindCodec codec, object publishedContent, string? orderKey,
        bool childrenLocal, JsonObject? unknown) =>
        DataSyncContentForms.ComparisonForm(codec, publishedContent, orderKey, childrenLocal, unknown);

    /// <summary>
    /// The comparison-form hash of a peer record with this build's codec (§3.4,
    /// <see cref="DataSyncPublication.SharedHashOfRecord"/>): null for a tombstone, a record held at its source, or
    /// one this build holds (a newer schema, invalid content).
    /// </summary>
    public static string? RecordSharedHash(IDataSyncKindCodec codec, DataSyncWireRecord record,
        DataSyncLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(record);
        return DataSyncPublication.SharedHashOfRecord(codec, record, limits ?? DataSyncLimits.Default);
    }

    /// <summary>
    /// §2.8 <c>resultEqualsRemote</c> (§6.4): whether what this device holds after an apply, as re-read, compares equal
    /// to the peer's record. False is normalization drift the codec does not model: the revision then adds this device's
    /// own counter, and the peer fast-forwards to the normalized content once.
    /// </summary>
    public static bool ResultEqualsRemote(IDataSyncKindCodec codec, DataSyncEntityForm reread, DataSyncWireRecord remote) =>
        reread.SharedHash is { } shared && !IsHeldMarker(shared) &&
        string.Equals(shared, RecordSharedHash(codec, remote), StringComparison.Ordinal);

    /// <summary><c>UnknownJson</c> (§8.9): the preserved unknown top-level members, or null.</summary>
    public static JsonObject? ReadUnknown(string? unknownJson)
    {
        if (string.IsNullOrEmpty(unknownJson)) return null;
        try
        {
            return JsonNode.Parse(unknownJson) as JsonObject ??
                   throw new InvalidDataException("The stored UnknownJson is not an object.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The stored UnknownJson is corrupted: {e.Message}", e);
        }
    }
}
