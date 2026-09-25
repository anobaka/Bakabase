using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// Inbox drafts (§9.1): the subject, origin and token of an item. Every item has a subject
/// <c>(kind, SyncKey, type, subjectPath)</c> and an origin: merger-derived items are re-derived whenever their
/// entity is evaluated; state-derived items are tied to local state (§9.3).
/// </summary>
public static class DataSyncInboxDrafts
{
    /// <summary>Subject of an item about the whole entity.</summary>
    public const string EntitySubject = "";

    /// <summary>Subject of a <c>TypeChange</c> item: the path <c>type</c>.</summary>
    public const string TypeSubject = "type";

    /// <summary>Subject of the link-level <c>LargeChange</c> item (<c>Kind = ""</c>, <see cref="SyncKey.LinkLevel"/>).</summary>
    public const string LargeChangeSubject = "largeChange";

    /// <summary><see cref="DataSyncInboxPayload.Detail"/> of <c>DeletedHereEditedThere</c>: the peer's version dominates the tombstone.</summary>
    public const string DetailRestored = "restored";

    /// <summary><see cref="DataSyncInboxPayload.Detail"/> of <c>DeletedHereEditedThere</c>: concurrent with the tombstone.</summary>
    public const string DetailChangedAfterDelete = "changedAfterDelete";

    /// <summary>At most this many children or large-change entries in one payload (§2.7).</summary>
    public const int MaxListed = 500;

    /// <summary>
    /// The origin of an item type (§9.1): <c>ChildDeletedInUse</c> (a hold), <c>MassChildDeletion</c> (a frozen
    /// entity), <c>SuspectedLostUpdate</c> (<c>PublishHeld</c>) and <c>LargeChange</c> (waiting records) are
    /// state-derived; every other type is merger-derived.
    /// </summary>
    public static DataSyncInboxItemOrigin OriginOf(DataSyncInboxItemType type) => type switch
    {
        DataSyncInboxItemType.ChildDeletedInUse or DataSyncInboxItemType.MassChildDeletion
            or DataSyncInboxItemType.SuspectedLostUpdate or DataSyncInboxItemType.LargeChange => DataSyncInboxItemOrigin.State,
        _ => DataSyncInboxItemOrigin.Merger,
    };

    /// <summary>
    /// A child path of §8.5.1 (<c>choice:{id}</c>, <c>tag:{id}</c>, <c>node:{id}</c>, their <c>:color</c> and
    /// <c>:parent</c>, a test kind's <c>child:{id}</c>): any path with a <c>:</c> that is not an unknown member
    /// (<c>x:</c>) or an extension (<c>ext:</c>). A conflict on it is a <c>ChildRenameConflict</c>; on any other path
    /// a <c>FieldConflict</c>.
    /// </summary>
    public static bool IsChildPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Contains(':') && !path.StartsWith(DataSyncUnknownMembers.PathPrefix, StringComparison.Ordinal) &&
               !path.StartsWith("ext:", StringComparison.Ordinal);
    }

    /// <summary>The item type of a conflicting field (§9.1 A and the child rename conflicts).</summary>
    public static DataSyncInboxItemType ConflictTypeOf(string path) =>
        IsChildPath(path) ? DataSyncInboxItemType.ChildRenameConflict : DataSyncInboxItemType.FieldConflict;

    /// <summary>
    /// The item token (§9.2): the first 32 hex characters of SHA-256 over the canonical JSON
    /// <c>{"fields":[{"base","local","path","remote"}],"subjectPath","type"}</c>, with <c>type</c> as the enum name
    /// and display values as canonical objects. It holds no record hash, vector or usage count, so an unrelated peer
    /// edit never invalidates a card.
    /// </summary>
    public static string Token(DataSyncInboxItemType type, string subjectPath, IReadOnlyList<DataSyncFieldOutcome> fields)
    {
        ArgumentNullException.ThrowIfNull(subjectPath);
        ArgumentNullException.ThrowIfNull(fields);
        var json = new JsonObject
        {
            ["fields"] = new JsonArray(fields.Select(f => (JsonNode?)new JsonObject
            {
                ["base"] = ToJson(f.Base),
                ["local"] = ToJson(f.Local),
                ["path"] = f.Path,
                ["remote"] = ToJson(f.Remote),
            }).ToArray()),
            ["subjectPath"] = subjectPath,
            ["type"] = type.ToString(),
        };
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(json)));
        return Convert.ToHexStringLower(hash)[..32];
    }

    /// <summary>A draft with its origin (<see cref="OriginOf"/>) and token (<see cref="Token"/>).</summary>
    public static DataSyncInboxDraft Create(string kind, SyncKey key, string? localKey, DataSyncInboxItemType type,
        string subjectPath, DataSyncInboxPayload payload, string? recordHash, DataSyncVersionVector? recordVv,
        DataSyncVersionVector? localVv, DataSyncMergeFlags flags)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(payload);
        return new DataSyncInboxDraft(kind, key, localKey, type, OriginOf(type), subjectPath, payload, recordHash,
            recordVv, localVv, flags, Token(type, subjectPath, payload.Fields));
    }

    /// <summary>
    /// The one hash a multi-record item (row M) refers to: <c>ContentHash</c> of the ordinally sorted record hashes.
    /// </summary>
    public static string CombinedRecordHash(IEnumerable<string> recordHashes)
    {
        ArgumentNullException.ThrowIfNull(recordHashes);
        var sorted = recordHashes.OrderBy(h => h, StringComparer.Ordinal).Select(h => (JsonNode?)JsonValue.Create(h));
        return ContentHash.Of(new JsonArray(sorted.ToArray()));
    }

    private static JsonNode? ToJson(DataSyncDisplayValue? value)
    {
        if (value is null) return null;
        var json = new JsonObject();
        if (value.Text is not null) json["text"] = value.Text;
        if (value.Color is not null) json["color"] = value.Color;
        if (value.Group is not null) json["group"] = value.Group;
        if (value.Path is not null) json["path"] = new JsonArray(value.Path.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        if (value.Flag is { } flag) json["flag"] = flag;
        if (value.Number is { } number) json["number"] = number;
        return json;
    }
}
