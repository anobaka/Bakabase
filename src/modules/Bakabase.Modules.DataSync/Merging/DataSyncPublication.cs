using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// What one local entity publishes and how it compares (§3.4, §3.5), computed once by the same code for Refresh
/// [C], the feed [C], the merger and the simulator, so none of them can drift from the others.
/// </summary>
/// <param name="Published">The codec's validated published content; null when held.</param>
/// <param name="Content">
/// The record's <c>Content</c>: <c>codec.Write(Published)</c> with the preserved unknown members merged back (§3.5
/// step 5); null when held.
/// </param>
/// <param name="Hash"><c>ContentHash(Content)</c>, the record's <c>hash</c>; null when held.</param>
/// <param name="SharedHash">The comparison form's hash (§3.4); null when held.</param>
/// <param name="Held">The reader would hold the entity (§3.5 step 4); it is published as <c>HeldAtSource</c>.</param>
/// <param name="HeldDetail">Why, for this device only (e.g. <c>tooManyChildren</c>); never on the wire.</param>
public sealed record DataSyncPublication(object? Published, JsonObject? Content, string? Hash, string? SharedHash,
    DataSyncHeldReason? Held, string? HeldDetail, int ChildrenWithheld, IReadOnlyList<DataSyncPlanWarning> Warnings)
{
    /// <summary>
    /// Publishes local content (§3.5): <c>codec.Publish(localContent, overlay, childrenLocal)</c>, then the record
    /// content with the unknown members and its hash, then the comparison form's hash with the order key (§3.4).
    /// </summary>
    /// <param name="localContent">The entity's <c>ReadLocal</c> content (every local child).</param>
    public static DataSyncPublication Of(IDataSyncKindCodec codec, object localContent, DataSyncOverlay overlay,
        bool childrenLocal, string? orderKey, JsonObject? unknown)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(localContent);
        ArgumentNullException.ThrowIfNull(overlay);
        var publishable = codec.Publish(localContent, overlay, childrenLocal);
        if (publishable.Held is { } held || publishable.Content is null)
        {
            return new DataSyncPublication(null, null, null, null, publishable.Held ?? DataSyncHeldReason.Invalid,
                publishable.HeldDetail, publishable.ChildrenWithheld, publishable.Warnings);
        }

        var content = DataSyncContentForms.PublishedContent(codec, publishable.Content, unknown);
        return new DataSyncPublication(publishable.Content, content, ContentHash.Of(content),
            DataSyncContentForms.SharedHash(codec, publishable.Content, orderKey, childrenLocal, unknown), null, null,
            publishable.ChildrenWithheld, publishable.Warnings);
    }

    /// <summary>
    /// The comparison-form hash of a peer record's content as this build reads it (§3.4: every device computes it
    /// with its own codec; it is never read from the wire). Null for a tombstone, a held record, or content this
    /// build would hold.
    /// </summary>
    public static string? SharedHashOfRecord(IDataSyncKindCodec codec, DataSyncWireRecord record, DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(record);
        var read = DataSyncRecordValidation.ReadContent(codec, record, limits);
        return read?.Content is null
            ? null
            : DataSyncContentForms.SharedHash(codec, read.Content, record.OrderKey,
                DataSyncRecordValidation.ChildrenLocalOf(record.Content), read.Unknown);
    }
}
