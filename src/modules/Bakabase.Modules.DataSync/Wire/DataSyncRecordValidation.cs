using System.Globalization;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Wire;

/// <summary>
/// One whole record through this build's codec (v3.1 §6.3 per-entity rules): the hash check, the schema check and
/// upgrade, then the codec's validated <c>Read</c>. <see cref="DataSyncRecordAssembler"/> stages a pull with it after
/// reassembling chunks, and the merger re-stages stored pending records with it (§8.4), so a record is judged the
/// same way whether it arrived now or waited. Never throws on peer input.
/// </summary>
public static class DataSyncRecordValidation
{
    /// <summary>
    /// Stages one record whose content is complete (no chunks outstanding). A tombstone takes no codec; a record
    /// held at the source is <c>Held(AtSource)</c>; a missing codec holds it as <c>UnknownKind</c>.
    /// </summary>
    /// <param name="index">Its position in the kind, for the fallback display name <c>#index</c>.</param>
    public static DataSyncIncomingEntity Stage(IDataSyncKindCodec? codec, DataSyncWireRecord record, int index,
        DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(limits);
        var fallbackName = "#" + index.ToString(CultureInfo.InvariantCulture);
        if (record.Deleted) return new DataSyncIncomingEntity(record, null, null, null, fallbackName, null, []);
        if (record.HeldAtSource is not null)
            return new DataSyncIncomingEntity(record, null, null, null, NameOrFallback(record.Content, fallbackName, limits),
                DataSyncHeldReason.AtSource, []);
        if (record.Content is not { } content || record.Chunks > 0)
            return Held(record, record.Content, fallbackName, DataSyncHeldReason.Invalid, [], limits);
        if (codec is null) return Held(record, content, fallbackName, DataSyncHeldReason.UnknownKind, [], limits);
        if (record.Hash != ContentHashOf(content))
            return Held(record, content, fallbackName, DataSyncHeldReason.Invalid, [], limits);

        var descriptor = codec.Descriptor;
        var current = (JsonObject)content.DeepClone();
        if (record.SchemaVersion > descriptor.SchemaVersion)
            return Held(record, content, fallbackName, DataSyncHeldReason.NewerSchema, [], limits);
        if (record.SchemaVersion < descriptor.SchemaVersion)
        {
            try
            {
                current = codec.Upgrade(current, record.SchemaVersion);
            }
            catch (DataSyncHeldException e)
            {
                return Held(record, content, fallbackName, e.Reason, [], limits);
            }
        }

        var read = codec.Read(current, limits);
        if (read.Held is { } held) return Held(record, content, fallbackName, held, read.Warnings, limits);

        var typed = read.Content!;
        var name = codec.NameOf(typed);
        return new DataSyncIncomingEntity(record, typed, read.Unknown, Canonical.ContentHash.Of(codec.Write(typed)),
            string.IsNullOrEmpty(name) ? fallbackName : name, null, read.Warnings);
    }

    /// <summary>
    /// A record's content as this build reads it (§3.4: every device computes forms with its own codec): the typed
    /// validated content and its preserved unknown members, or null when this build would hold it. Used for the
    /// peer content a base keeps.
    /// </summary>
    public static CodecReadResult? ReadContent(IDataSyncKindCodec codec, DataSyncWireRecord record, DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(record);
        if (record.Deleted || record.HeldAtSource is not null || record.Content is null || record.Chunks > 0) return null;
        var content = (JsonObject)record.Content.DeepClone();
        if (record.SchemaVersion > codec.Descriptor.SchemaVersion) return null;
        if (record.SchemaVersion < codec.Descriptor.SchemaVersion)
        {
            try
            {
                content = codec.Upgrade(content, record.SchemaVersion);
            }
            catch (DataSyncHeldException)
            {
                return null;
            }
        }

        var read = codec.Read(content, limits);
        return read.Content is null ? null : read;
    }

    /// <summary>Whether published content says "sync the definition only" (§3.6): <c>"childrenLocal": true</c>.</summary>
    public static bool ChildrenLocalOf(JsonObject? content) =>
        content?["childrenLocal"] is JsonValue flag && flag.TryGetValue<bool>(out var on) && on;

    internal static string ContentHashOf(JsonObject content) => Canonical.ContentHash.Of(content);

    private static DataSyncIncomingEntity Held(DataSyncWireRecord record, JsonObject? content, string fallbackName,
        DataSyncHeldReason reason, IReadOnlyList<DataSyncPlanWarning> warnings, DataSyncLimits limits) =>
        new(record, null, null, null, NameOrFallback(content, fallbackName, limits), reason, warnings);

    /// <summary>Every kind's content has a top-level string name by convention; show it when it is readable.</summary>
    private static string NameOrFallback(JsonObject? content, string fallbackName, DataSyncLimits limits) =>
        content is not null && DataSyncWireFormat.TryGetString(content, "name", out var n) && n.Length is > 0 &&
        n.Length <= limits.MaxNameLength && DataSyncWireFormat.IsDisplayText(n)
            ? n
            : fallbackName;
}
