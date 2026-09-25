using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.Modules.DataSync.Canonical;

/// <summary>
/// The two JSON forms every kind shares around its codec: the content a record carries (§3.5 steps 5–6) and the
/// comparison form behind <c>SharedHash</c> (§3.4). Both add the preserved unknown top-level members (§8.9)
/// verbatim, and neither ever lets one override a member the codec wrote or declares.
/// </summary>
/// <remarks>
/// These are the codec's own unknown-aware members (<see cref="IDataSyncKindCodec.WritePublished"/>, the four-argument
/// <see cref="IDataSyncKindCodec.ComparisonForm(object, string?, bool, JsonObject?)"/> and
/// <see cref="IDataSyncKindCodec.SharedHash"/>), so a kind that declares its known members is honoured wherever a form
/// is computed.
/// </remarks>
public static class DataSyncContentForms
{
    /// <summary>
    /// A record's <c>Content</c> (§3.5 step 5): <c>codec.Write(published)</c> with the preserved unknown members
    /// merged back at the top level. <c>Hash = ContentHash.Of(result)</c>.
    /// </summary>
    /// <param name="publishedContent">The codec's validated published content (<c>Publish(...).Content</c>).</param>
    public static JsonObject PublishedContent(IDataSyncKindCodec codec, object publishedContent, JsonObject? unknown)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(publishedContent);
        return codec.WritePublished(publishedContent, unknown);
    }

    /// <summary>
    /// The comparison form (§3.4): <c>codec.ComparisonForm(...)</c> with the preserved unknown members added
    /// verbatim. Every device computes it with its own codec, for its own entities and for every peer record it
    /// compares against; it is never read from the wire.
    /// </summary>
    public static JsonObject ComparisonForm(IDataSyncKindCodec codec, object publishedContent, string? orderKey,
        bool childrenLocal, JsonObject? unknown)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(publishedContent);
        return codec.ComparisonForm(publishedContent, orderKey, childrenLocal, unknown);
    }

    /// <summary><c>SharedHash = ContentHash(ComparisonForm(...))</c> (§3.4).</summary>
    public static string SharedHash(IDataSyncKindCodec codec, object publishedContent, string? orderKey,
        bool childrenLocal, JsonObject? unknown)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(publishedContent);
        return codec.SharedHash(publishedContent, orderKey, childrenLocal, unknown);
    }

    /// <summary>
    /// Adds the preserved unknown members to a form or content the codec wrote, in ordinal order, never over a member
    /// it already has, one of <paramref name="knownMembers"/>, or <c>orderKey</c> (the form's own member for every kind
    /// with order, §3.7, never content). The codec's node is never changed: a copy is returned when anything is added.
    /// </summary>
    public static JsonObject WithUnknown(JsonObject written, JsonObject? unknown, IReadOnlyCollection<string> knownMembers)
    {
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(knownMembers);
        if (unknown is null || unknown.Count == 0) return written;
        JsonObject? result = null;
        foreach (var (name, value) in unknown.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (written.ContainsKey(name) || knownMembers.Contains(name) || name == OrderKeyMember) continue;
            // A codec may hand out a node it keeps; never add to it.
            result ??= (JsonObject)written.DeepClone();
            result[name] = value?.DeepClone();
        }

        return result ?? written;
    }

    private const string OrderKeyMember = "orderKey";
}
