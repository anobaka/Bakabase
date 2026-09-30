using System.Globalization;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Wire;

/// <summary>
/// One whole record through this build's codec (v3.1 §6.3 per-entity rules): the hash check, the schema check, then
/// the codec's validated <c>Read</c>. <see cref="DataSyncRecordAssembler"/> stages a pull with it, and the merger re-stages stored pending records with it (§8.4), so a record is judged the
/// same way whether it arrived now or waited. Never throws on peer input.
/// </summary>
public static class DataSyncRecordValidation
{
    /// <summary>
    /// Stages one record. A tombstone takes no codec; a record held at the source is <c>Held(AtSource)</c>, or
    /// <c>Held(TooLarge)</c> when the source said it is too large to travel; a missing codec holds it as
    /// <c>UnknownKind</c>.
    /// </summary>
    /// <param name="index">Its position in the kind, for the fallback display name <c>#index</c>.</param>
    public static DataSyncIncomingEntity Stage(IDataSyncKindCodec? codec, DataSyncWireRecord record, int index,
        DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(limits);
        var fallbackName = "#" + index.ToString(CultureInfo.InvariantCulture);
        if (record.Deleted) return new DataSyncIncomingEntity(record, null, fallbackName, null, []);
        if (record.HeldAtSource is not null)
            return new DataSyncIncomingEntity(record, null, NameOrFallback(record.Content, fallbackName, limits),
                record.HeldAtSource == DataSyncHeldReason.TooLarge ? DataSyncHeldReason.TooLarge : DataSyncHeldReason.AtSource,
                []);
        if (record.Content is not { } content)
            return Held(record, record.Content, fallbackName, DataSyncHeldReason.Invalid, [], limits);
        if (codec is null) return Held(record, content, fallbackName, DataSyncHeldReason.UnknownKind, [], limits);
        if (record.Hash != ContentHashOf(content))
            return Held(record, content, fallbackName, DataSyncHeldReason.Invalid, [], limits);

        // One contract, one schema (§8.12): a record of any other schema version is never read, whether it arrived
        // now or was stored before this build.
        if (record.SchemaVersion != codec.Descriptor.SchemaVersion)
        {
            return Held(record, content, fallbackName, record.SchemaVersion > codec.Descriptor.SchemaVersion
                ? DataSyncHeldReason.NewerSchema
                : DataSyncHeldReason.Invalid, [], limits);
        }

        var read = codec.Read((JsonObject)content.DeepClone(), limits);
        if (read.Held is { } held) return Held(record, content, fallbackName, held, read.Warnings, limits);

        var typed = read.Content!;
        var name = codec.NameOf(typed);
        return new DataSyncIncomingEntity(record, typed, string.IsNullOrEmpty(name) ? fallbackName : name, null,
            read.Warnings);
    }

    /// <summary>
    /// A record's content as this build reads it (§3.4: every device computes forms with its own codec): the typed
    /// validated content, or null when this build would hold it (another schema version included). Used for the
    /// peer content a base keeps.
    /// </summary>
    public static CodecReadResult? ReadContent(IDataSyncKindCodec codec, DataSyncWireRecord record, DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(record);
        if (record.Deleted || record.HeldAtSource is not null || record.Content is null ||
            record.SchemaVersion != codec.Descriptor.SchemaVersion) return null;
        var read = codec.Read((JsonObject)record.Content.DeepClone(), limits);
        return read.Content is null ? null : read;
    }

    /// <summary>Whether published content says "sync the definition only" (§3.6): <c>"childrenLocal": true</c>.</summary>
    public static bool ChildrenLocalOf(JsonObject? content) =>
        content?["childrenLocal"] is JsonValue flag && flag.TryGetValue<bool>(out var on) && on;

    internal static string ContentHashOf(JsonObject content) => Canonical.ContentHash.Of(content);

    private static DataSyncIncomingEntity Held(DataSyncWireRecord record, JsonObject? content, string fallbackName,
        DataSyncHeldReason reason, IReadOnlyList<DataSyncPlanWarning> warnings, DataSyncLimits limits) =>
        new(record, null, NameOrFallback(content, fallbackName, limits), reason, warnings);

    /// <summary>Every kind's content has a top-level string name by convention; show it when it is readable.</summary>
    private static string NameOrFallback(JsonObject? content, string fallbackName, DataSyncLimits limits) =>
        content is not null && DataSyncWireFormat.TryGetString(content, "name", out var n) && n.Length is > 0 &&
        n.Length <= limits.MaxNameLength && DataSyncWireFormat.IsDisplayText(n)
            ? n
            : fallbackName;
}
