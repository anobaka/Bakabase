using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.DataSync.Merging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Bakabase.Modules.DataSync.Tests.CustomProperties.Cp;

namespace Bakabase.Modules.DataSync.Tests.CustomProperties;

/// <summary>Builders and assertions for the custom property <c>Merge3</c> tests (§8.5).</summary>
internal static class M3
{
    public const int LinkId = 7;

    /// <summary>
    /// Merges with the arguments a merger passes. Defaults: the base and the remote are validated as peer content, the
    /// child map is the identity over the base's option ids (what a sync leaves behind), every local option is unused.
    /// </summary>
    public static DataSyncMerge3Result Merge(CustomPropertyContentV1? b, CustomPropertyContentV1 l,
        CustomPropertyContentV1 r, DataSyncMerge3Mode mode3 = DataSyncMerge3Mode.ThreeWay,
        IReadOnlyDictionary<string, string>? childMap = null, DataSyncOverlay? overlay = null,
        DataSyncLinkMode mode = DataSyncLinkMode.TwoWay, bool lastEditorIsSelf = true,
        DataSyncMergeSide winner = DataSyncMergeSide.Local, IReadOnlyDictionary<string, int>? usage = null,
        DataSyncChildDeletionMode deletions = DataSyncChildDeletionMode.Normal, bool localChildrenLocal = false,
        bool baseChildrenLocal = false, CustomPropertyCodec? codec = null) =>
        ((IDataSyncKindCodec)(codec ?? Codec)).Merge3(Input(b, l, r, mode3, childMap, overlay, mode, lastEditorIsSelf,
            winner, usage, deletions, localChildrenLocal, baseChildrenLocal));

    public static DataSyncMerge3Input Input(CustomPropertyContentV1? b, CustomPropertyContentV1 l,
        CustomPropertyContentV1 r, DataSyncMerge3Mode mode3 = DataSyncMerge3Mode.ThreeWay,
        IReadOnlyDictionary<string, string>? childMap = null, DataSyncOverlay? overlay = null,
        DataSyncLinkMode mode = DataSyncLinkMode.TwoWay, bool lastEditorIsSelf = true,
        DataSyncMergeSide winner = DataSyncMergeSide.Local, IReadOnlyDictionary<string, int>? usage = null,
        DataSyncChildDeletionMode deletions = DataSyncChildDeletionMode.Normal, bool localChildrenLocal = false,
        bool baseChildrenLocal = false) =>
        new(b is null ? null : Peer(b), l, overlay ?? DataSyncOverlay.None, Peer(r), mode3,
            childMap ?? (b is null ? new Dictionary<string, string>() : IdentityMap(b)), localChildrenLocal,
            baseChildrenLocal, mode, lastEditorIsSelf, winner, usage ?? Unused(l), deletions);

    /// <summary>Content as a reader validates it: what a peer's record and a base hold.</summary>
    public static CustomPropertyContentV1 Peer(CustomPropertyContentV1 content) => ReadValid(Codec.Write(content)).Content;

    public static Dictionary<string, string> IdentityMap(CustomPropertyContentV1 content) =>
        Ids(content).Distinct().ToDictionary(id => id, id => id, StringComparer.Ordinal);

    public static Dictionary<string, int> Unused(CustomPropertyContentV1 content) =>
        Ids(content).Distinct().ToDictionary(id => id, _ => 0, StringComparer.Ordinal);

    public static IEnumerable<string> Ids(CustomPropertyContentV1 content) =>
        Codec.ChildrenOf(content).Select(c => c.Id);

    public static CustomPropertyContentV1 Merged(DataSyncMerge3Result result) => (CustomPropertyContentV1)result.Merged;

    /// <summary>The comparison form of what this device publishes after the merge: its holds are overlays too.</summary>
    public static string PublishedForm(DataSyncMerge3Result result, DataSyncOverlay? overlay = null,
        bool childrenLocal = false)
    {
        overlay ??= DataSyncOverlay.None;
        var held = overlay.HeldChildren.Concat(result.HeldChildIds.Select(id => new DataSyncHeldChild(id, LinkId)))
            .Where(h => !result.ReleasedChildIds.Contains(h.ChildId)).ToArray();
        var publishable = Untyped.Publish(result.Merged, new DataSyncOverlay(overlay.LocalOnlyChildren, held), childrenLocal);
        Assert.IsNull(publishable.Held, publishable.HeldDetail);
        return Form((CustomPropertyContentV1)publishable.Content!, null, childrenLocal);
    }

    public static DataSyncFieldOutcome Field(DataSyncMerge3Result result, string path) =>
        result.Fields.SingleOrDefault(f => f.Path == path)
        ?? throw new AssertFailedException(
            $"no outcome for {path}; have {string.Join(", ", result.Fields.Select(f => $"{f.Path}={f.Resolution}"))}");

    public static void NoField(DataSyncMerge3Result result, string path) =>
        Assert.IsFalse(result.Fields.Any(f => f.Path == path),
            $"unexpected outcome for {path}: {result.Fields.First(f => f.Path == path).Resolution}");

    public static void NoConflicts(DataSyncMerge3Result result) =>
        Assert.IsFalse(result.Fields.Any(f => f.Resolution == DataSyncFieldResolution.Conflict),
            string.Join(", ", result.Fields.Where(f => f.Resolution == DataSyncFieldResolution.Conflict).Select(f => f.Path)));

    public static string[] ChoiceIds(DataSyncMerge3Result result) => Merged(result).Choices.Select(c => c.Uuid!).ToArray();

    public static string[] ChoiceLabels(DataSyncMerge3Result result) => Merged(result).Choices.Select(c => c.Label).ToArray();

    /// <summary>A multilevel tree as <c>label(child,child)</c> text, ids left out.</summary>
    public static string Shape(IReadOnlyList<CustomPropertyNodeV1> nodes) =>
        string.Join(",", nodes.Select(n => n.Children.Count == 0 ? n.Label : $"{n.Label}({Shape(n.Children)})"));

    public static string Shape(DataSyncMerge3Result result) => Shape(Merged(result).Nodes);

    public static DataSyncOverlay Held(params string[] ids) =>
        new([], ids.Select(id => new DataSyncHeldChild(id, LinkId)).ToArray());

    public static DataSyncOverlay LocalOnly(params string[] ids) => new(ids, []);
}
