using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Kinds.ExtensionGroups;

/// <summary>
/// Codec of kind <c>extensionGroup</c> (v3.1 §3.4, §3.8 and §8.5.3 here). Extensions are values, not children
/// with ids: <c>ChildMap</c> is always empty, overlays name canonical extension strings, and removing one is a
/// safe change that never depends on usage.
/// </summary>
public sealed partial class ExtensionGroupCodec : DataSyncKindCodec<ExtensionGroupContentV1>
{
    public const int SchemaVersion = 1;

    /// <summary>Change id prefix of an added extension (v3.1 §7.4): <c>ext:add:{extension}</c>.</summary>
    public const string AddChangePrefix = "ext:add:";

    /// <summary>Merge path of one extension (§8.5.1): <c>ext:{extension}</c>.</summary>
    public const string ExtensionPathPrefix = "ext:";

    private const string NameMember = "name";
    private const string ExtensionsMember = "extensions";

    public static ExtensionGroupCodec Instance { get; } = new();

    public override DataSyncKindDescriptor Descriptor { get; } = new(DataSyncKindIds.ExtensionGroup, SchemaVersion, [],
        typeof(ExtensionGroupContentV1), AutoLinkIdentical: true, HasOrder: false, HasChildren: true,
        SupportsChildrenLocal: false, ChildNoun: "extension");

    /// <summary>Bump whenever <see cref="ComparisonForm"/> changes for any input (§3.4).</summary>
    public override int ComparisonFormVersion => 1;

    // ---- peer input -------------------------------------------------------------------------

    protected override CodecReadResult ReadCore(JsonObject content, DataSyncLimits limits)
    {
        var warnings = new List<DataSyncPlanWarning>();
        JsonObject? unknown = null;
        foreach (var (member, value) in content.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (member is NameMember or ExtensionsMember) continue;
            (unknown ??= new JsonObject())[member] = value?.DeepClone();
        }

        if (unknown is not null)
        {
            warnings.Add(new DataSyncPlanWarning(DataSyncWarningCode.UnknownFieldsIgnored, null,
                new Dictionary<string, string> { ["count"] = unknown.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }

        if (!TryGetString(content, NameMember, out var name)) return Invalid("name is missing", warnings, unknown);
        if (name.Length == 0 || name.Length > limits.MaxNameLength || !IsCleanText(name))
            return Invalid("name is invalid", warnings, unknown);
        if (content[ExtensionsMember] is not JsonArray array) return Invalid("extensions are missing", warnings, unknown);
        if (array.Count > limits.MaxOptionsPerProperty) return Invalid("tooManyChildren", warnings, unknown);

        var extensions = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.TryGetValue(out string? raw) &&
                !string.IsNullOrWhiteSpace(raw))
            {
                var extension = ExtensionGroupContentV1.Canonicalize(raw);
                if (IsValidExtension(extension, limits))
                {
                    extensions.Add(extension);
                    continue;
                }
            }

            warnings.Add(new DataSyncPlanWarning(DataSyncWarningCode.OptionDropped, null,
                new Dictionary<string, string> { ["reason"] = "extension", ["descendants"] = "0" }));
        }

        return new CodecReadResult(new ExtensionGroupContentV1(name, extensions), null, [], warnings, unknown);
    }

    /// <summary>
    /// A canonical extension a reader accepts (v3.1 §6.3): at most <c>MaxExtensionLength</c> characters, matching
    /// <c>^\.[^\s/\\:*?"&lt;&gt;|,]{1,31}$</c>, with no control character or unpaired surrogate.
    /// </summary>
    public static bool IsValidExtension(string extension, DataSyncLimits limits)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(limits);
        return extension.Length >= 2 && extension.Length <= limits.MaxExtensionLength &&
               ExtensionPattern().IsMatch(extension) && IsCleanText(extension);
    }

    // ---- local content ----------------------------------------------------------------------

    public override ExtensionGroupContentV1 ReadLocal(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!TryGetString(content, NameMember, out var name))
            throw new InvalidOperationException("Local extension group content has no name.");
        if (content[ExtensionsMember] is not JsonArray array)
            throw new InvalidOperationException("Local extension group content has no extensions.");
        return new ExtensionGroupContentV1(name, array.Select(e => e is JsonValue v && v.TryGetValue(out string? s)
            ? s
            : throw new InvalidOperationException("A local extension is not a string.")));
    }

    public override JsonObject Write(ExtensionGroupContentV1 content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new JsonObject
        {
            [ExtensionsMember] = new JsonArray(content.Extensions.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()),
            [NameMember] = content.Name,
        };
    }

    public override string NameOf(ExtensionGroupContentV1 content) => content.Name;
    public override int ChildCountOf(ExtensionGroupContentV1 content) => content.Extensions.Count;

    public override IReadOnlyList<DataSyncChildInfo> ChildrenOf(ExtensionGroupContentV1 content) =>
        content.Extensions.Select(e => new DataSyncChildInfo(e, null, new DataSyncDisplayValue(e))).ToList();

    // ---- first contact (review) -------------------------------------------------------------

    public override DataSyncNaturalMatch MatchNatural(ExtensionGroupContentV1 incoming, ExtensionGroupContentV1 local)
    {
        if (incoming.Name == local.Name)
        {
            return incoming.Extensions.SequenceEqual(local.Extensions, StringComparer.Ordinal)
                ? DataSyncNaturalMatch.Identical
                : DataSyncNaturalMatch.Exact;
        }

        // Extension groups have no subtype, so a looser name match is Similar and never a Clash.
        return string.Equals(incoming.Name.Trim(), local.Name.Trim(), StringComparison.OrdinalIgnoreCase)
            ? DataSyncNaturalMatch.Similar
            : DataSyncNaturalMatch.None;
    }

    public override EntityDiff Diff(ExtensionGroupContentV1 local, ExtensionGroupContentV1 incoming)
    {
        var changes = new List<DataSyncFieldChange>();
        if (local.Name != incoming.Name)
        {
            changes.Add(new DataSyncFieldChange(NameMember, DataSyncFieldChangeKind.Set, NameMember,
                new DataSyncDisplayValue(local.Name), new DataSyncDisplayValue(incoming.Name), null, null));
        }

        var localSet = local.Extensions.ToHashSet(StringComparer.Ordinal);
        var unchanged = 0;
        foreach (var extension in incoming.Extensions)
        {
            if (localSet.Contains(extension))
            {
                unchanged++;
                continue;
            }

            changes.Add(new DataSyncFieldChange(AddChangePrefix + extension, DataSyncFieldChangeKind.AddMember,
                ExtensionsMember, null, new DataSyncDisplayValue(extension), null, null));
        }

        return new EntityDiff(changes, [], unchanged, local.Extensions.Count - unchanged);
    }

    /// <summary>
    /// Local extensions are all kept (valid or not); accepted adds join them. A stored raw extension keeps its case:
    /// the adapter writes the raw extensions plus <see cref="MergeResult.AddedChildIds"/> (v3.1 N10).
    /// </summary>
    public override MergeResult Merge(ExtensionGroupContentV1 local, ExtensionGroupContentV1 incoming,
        IReadOnlySet<string> acceptedChangeIds)
    {
        ArgumentNullException.ThrowIfNull(acceptedChangeIds);
        var name = acceptedChangeIds.Contains(NameMember) ? incoming.Name : local.Name;
        var localSet = local.Extensions.ToHashSet(StringComparer.Ordinal);
        var added = incoming.Extensions
            .Where(e => !localSet.Contains(e) && acceptedChangeIds.Contains(AddChangePrefix + e)).ToList();
        return new MergeResult(new ExtensionGroupContentV1(name, local.Extensions.Concat(added)),
            new Dictionary<string, string>(), added, []);
    }

    public override MergeResult PrepareCreate(ExtensionGroupContentV1 incoming, string? nameOverride) =>
        new(new ExtensionGroupContentV1(nameOverride ?? incoming.Name, incoming.Extensions),
            new Dictionary<string, string>(), incoming.Extensions.ToList(), []);

    // ---- continuous sync --------------------------------------------------------------------

    /// <summary>
    /// §3.5: overlay extensions out, then this build's own validated read, so exactly what a reader accepts goes
    /// out. Extension groups do not offer <c>childrenLocal</c>; it is ignored.
    /// </summary>
    public override DataSyncPublishable Publish(ExtensionGroupContentV1 localContent, DataSyncOverlay overlay,
        bool childrenLocal)
    {
        var hidden = overlay.HiddenChildIds.ToHashSet(StringComparer.Ordinal);
        var visible = localContent.Extensions.Where(e => !hidden.Contains(e)).ToList();
        var withheld = localContent.Extensions.Count - visible.Count;

        var read = Read(Write(new ExtensionGroupContentV1(localContent.Name, visible)), DataSyncLimits.Default);
        if (read.Held is { } held)
            return new DataSyncPublishable(null, withheld, read.Warnings.ToArray(), held, read.Errors.FirstOrDefault());

        var published = (ExtensionGroupContentV1)read.Content!;
        return new DataSyncPublishable(published, withheld + visible.Count - published.Extensions.Count,
            read.Warnings.ToArray());
    }

    /// <summary>
    /// §3.4: the canonical content itself — the name, and the canonical extension set. Extension groups have no
    /// order key and no <c>childrenLocal</c>.
    /// </summary>
    public override JsonObject ComparisonForm(ExtensionGroupContentV1 publishedContent, string? orderKey,
        bool childrenLocal) => Write(publishedContent);

    /// <summary>
    /// Removing an extension never depends on usage (§8.5.3, §8.6), so the merger has nothing to read first.
    /// </summary>
    protected override IReadOnlyList<string> ChildDeletionCandidates(ExtensionGroupContentV1? baseContent,
        ExtensionGroupContentV1 local, ExtensionGroupContentV1 remote, DataSyncChildCandidatesInput input) => [];

    /// <summary>
    /// §8.5.2 for <c>name</c>, §8.5.3 for the extension set:
    /// <list type="bullet">
    /// <item>ThreeWay: <c>(L ∪ (R − B)) − ((B − R) ∩ L)</c>: additions from either side win, and an extension is
    /// removed only when the peer removed it and this device still has it from the base. Never a conflict.</item>
    /// <item>FastForward: <c>R</c>. NoBase and Convert: <c>L ∪ R</c>.</item>
    /// </list>
    /// Overlay extensions are invisible to the merge and always kept. A held extension the peer re-added since the
    /// base is released.
    /// </summary>
    protected override DataSyncMerge3Result Merge3(ExtensionGroupContentV1? baseContent, ExtensionGroupContentV1 local,
        ExtensionGroupContentV1 remote, DataSyncMerge3Input input)
    {
        var fields = new List<DataSyncFieldOutcome>();
        var name = MergeName(baseContent, local, remote, input, fields);

        var hidden = input.LocalOverlay.HiddenChildIds.ToHashSet(StringComparer.Ordinal);
        var localSet = local.Extensions.ToHashSet(StringComparer.Ordinal);
        var visibleLocal = local.Extensions.Where(e => !hidden.Contains(e)).ToHashSet(StringComparer.Ordinal);
        var remoteSet = remote.Extensions.ToHashSet(StringComparer.Ordinal);
        var baseSet = baseContent?.Extensions.ToHashSet(StringComparer.Ordinal);

        HashSet<string> merged;
        switch (input.Mode3)
        {
            case DataSyncMerge3Mode.ThreeWay:
                merged = new HashSet<string>(visibleLocal, StringComparer.Ordinal);
                merged.UnionWith(remoteSet.Where(e => !baseSet!.Contains(e)));
                merged.ExceptWith(baseSet!.Where(e => !remoteSet.Contains(e) && visibleLocal.Contains(e)));
                break;
            case DataSyncMerge3Mode.FastForward:
                merged = new HashSet<string>(remoteSet, StringComparer.Ordinal);
                break;
            default:
                merged = new HashSet<string>(visibleLocal, StringComparer.Ordinal);
                merged.UnionWith(remoteSet);
                break;
        }

        // Overlay extensions stay whatever the peer did.
        merged.UnionWith(localSet.Where(hidden.Contains));

        foreach (var extension in visibleLocal.Union(remoteSet).Where(e => !hidden.Contains(e))
                     .OrderBy(e => e, StringComparer.Ordinal))
        {
            var inLocal = visibleLocal.Contains(extension);
            var inRemote = remoteSet.Contains(extension);
            if (inLocal == inRemote) continue;
            var inResult = merged.Contains(extension);
            fields.Add(new DataSyncFieldOutcome(ExtensionPathPrefix + extension,
                inResult == inRemote ? DataSyncFieldResolution.TookRemote : DataSyncFieldResolution.KeptLocal,
                Display(baseSet?.Contains(extension) == true, extension), Display(inLocal, extension),
                Display(inRemote, extension), Display(inResult, extension)));
        }

        // A hold ends when the peer re-added the extension since the base; without a base that cannot be told.
        var released = input.LocalOverlay.HeldChildren.Select(h => h.ChildId).Distinct(StringComparer.Ordinal)
            .Where(e => localSet.Contains(e) && remoteSet.Contains(e) &&
                        (baseSet is not null ? !baseSet.Contains(e) : input.Mode3 == DataSyncMerge3Mode.FastForward))
            .OrderBy(e => e, StringComparer.Ordinal).ToList();

        var result = new ExtensionGroupContentV1(name, merged);
        return new DataSyncMerge3Result(result, fields, new Dictionary<string, string>(),
            result.Extensions.Where(e => !localSet.Contains(e)).ToList(),
            local.Extensions.Where(e => !merged.Contains(e)).ToList(), [], released, [], [], false);
    }

    private static string MergeName(ExtensionGroupContentV1? baseContent, ExtensionGroupContentV1 local,
        ExtensionGroupContentV1 remote, DataSyncMerge3Input input, List<DataSyncFieldOutcome> fields)
    {
        string l = local.Name, r = remote.Name;
        if (l == r) return l;

        var b = baseContent?.Name;
        DataSyncFieldResolution resolution;
        if (input.Mode3 == DataSyncMerge3Mode.FastForward)
        {
            resolution = DataSyncFieldResolution.TookRemote;
        }
        else if (b is not null && input.Mode3 is DataSyncMerge3Mode.ThreeWay or DataSyncMerge3Mode.Convert)
        {
            resolution = l == b ? DataSyncFieldResolution.TookRemote
                : r == b ? DataSyncFieldResolution.KeptLocal
                : ConcurrentResolution(input);
        }
        else
        {
            // No base: a difference is concurrent.
            resolution = ConcurrentResolution(input);
        }

        var result = resolution is DataSyncFieldResolution.TookRemote or DataSyncFieldResolution.FollowTookRemote ? r : l;
        fields.Add(new DataSyncFieldOutcome(NameMember, resolution, b is null ? null : new DataSyncDisplayValue(b),
            new DataSyncDisplayValue(l), new DataSyncDisplayValue(r), new DataSyncDisplayValue(result)));
        return result;
    }

    /// <summary>Both sides changed a scalar (§8.5.2): Follow takes the peer's value unless the local one is another device's.</summary>
    private static DataSyncFieldResolution ConcurrentResolution(DataSyncMerge3Input input) =>
        input.Mode == DataSyncLinkMode.Follow && input.LocalLastEditorIsSelf
            ? DataSyncFieldResolution.FollowTookRemote
            : DataSyncFieldResolution.Conflict;

    private static DataSyncDisplayValue? Display(bool present, string extension) =>
        present ? new DataSyncDisplayValue(extension) : null;

    // ---- helpers ----------------------------------------------------------------------------

    private static CodecReadResult Invalid(string error, List<DataSyncPlanWarning> warnings, JsonObject? unknown) =>
        new(null, DataSyncHeldReason.Invalid, [error], warnings, unknown);

    private static bool TryGetString(JsonObject json, string member, out string value)
    {
        value = null!;
        if (json[member] is not JsonValue v || v.GetValueKind() != JsonValueKind.String ||
            !v.TryGetValue(out string? s)) return false;
        value = s;
        return true;
    }

    /// <summary>No U+0000 and no unpaired surrogate (v3.1 §6.3); tabs and line feeds are allowed.</summary>
    private static bool IsCleanText(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\0') return false;
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c)) return false;
        }

        return true;
    }

    [GeneratedRegex("^\\.[^\\s/\\\\:*?\"<>|,\\p{Cc}]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionPattern();
}
