using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.Modules.DataSync.Canonical;

/// <summary>
/// The two JSON forms every kind shares around its codec: the content a record carries (§3.5 steps 5–6) and the
/// comparison form behind <c>SharedHash</c> (§3.4). Both add the preserved unknown top-level members (§8.9)
/// verbatim, and neither ever lets one override a member the codec wrote.
/// </summary>
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
        return WithUnknown(codec.Write(publishedContent), unknown);
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
        return WithUnknown(codec.ComparisonForm(publishedContent, orderKey, childrenLocal), unknown);
    }

    /// <summary><c>SharedHash = ContentHash(ComparisonForm(...))</c> (§3.4).</summary>
    public static string SharedHash(IDataSyncKindCodec codec, object publishedContent, string? orderKey,
        bool childrenLocal, JsonObject? unknown) =>
        ContentHash.Of(ComparisonForm(codec, publishedContent, orderKey, childrenLocal, unknown));

    private static JsonObject WithUnknown(JsonObject known, JsonObject? unknown)
    {
        if (unknown is null || unknown.Count == 0) return known;
        // A codec may hand out a node it keeps; never add to it.
        known = (JsonObject)known.DeepClone();
        foreach (var (name, value) in unknown.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (!known.ContainsKey(name)) known[name] = value?.DeepClone();
        }

        return known;
    }
}
