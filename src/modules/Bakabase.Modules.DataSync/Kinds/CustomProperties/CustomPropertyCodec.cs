using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>
/// The codec of kind <c>customProperty</c>, schemaVersion 1 (v3.1 §3.3, §3.3–§3.7 here). Pure: it validates peer
/// content (<see cref="DataSyncKindCodec{TContent}.Read"/>), parses local content without validating
/// (<see cref="ReadLocal"/>), decides what this device publishes (<see cref="Publish"/>), computes the id-free,
/// class-folded comparison form (<see cref="ComparisonForm(CustomPropertyContentV1, string?, bool)"/>) and merges field by
/// field (<c>Merge3</c>, §8.5).
/// </summary>
public sealed partial class CustomPropertyCodec : DataSyncKindCodec<CustomPropertyContentV1>
{
    /// <summary>
    /// Bumped whenever <see cref="ComparisonForm(CustomPropertyContentV1, string?, bool)"/>'s output changes for any
    /// input (§3.4); <c>ComparisonFormGoldenTests</c> guards it.
    /// </summary>
    public const int CurrentComparisonFormVersion = 1;

    /// <summary>The codec with the default limits and policy: the one the adapter, the goldens and the inventory use.</summary>
    public static CustomPropertyCodec Instance { get; } = new();

    private readonly DataSyncLimits _limits;
    private readonly DataSyncAutoApplyPolicy _policy;

    /// <param name="limits">The reader's limits <see cref="Publish"/> applies (§3.5 step 4); the defaults when null.</param>
    /// <param name="policy">
    /// The B4 thresholds <c>Merge3</c> applies (§8.5.4 step 7), which <see cref="DataSyncMerge3Input"/> does not carry;
    /// the defaults when null.
    /// </param>
    public CustomPropertyCodec(DataSyncLimits? limits = null, DataSyncAutoApplyPolicy? policy = null)
    {
        _limits = limits ?? DataSyncLimits.Default;
        _policy = policy ?? DataSyncAutoApplyPolicy.Default;
    }

    internal DataSyncLimits Limits => _limits;

    public override DataSyncKindDescriptor Descriptor { get; } = new(
        DataSyncKindIds.CustomProperty, SchemaVersion: 1, DependsOn: [], typeof(CustomPropertyContentV1),
        AutoLinkIdentical: false, HasOrder: true, HasChildren: true, SupportsChildrenLocal: true, ChildNoun: "option");

    public override int ComparisonFormVersion => CurrentComparisonFormVersion;

    protected override IReadOnlyCollection<string> KnownContentMembers => CustomPropertyJson.ContentMembers;

    public override CustomPropertyContentV1 ReadLocal(JsonObject content) => CustomPropertyJson.ReadLocal(content);

    public override JsonObject Write(CustomPropertyContentV1 content) => CustomPropertyJson.Write(content);

    public override string NameOf(CustomPropertyContentV1 content) => content.Name;

    public override string? SubtypeOf(CustomPropertyContentV1 content) => CustomPropertyTypes.NameOf(content.Type);

    /// <summary>
    /// Label classes under <paramref name="from"/>'s IgnoreCase (<see cref="ChildClasses"/>,
    /// <see cref="DataSyncLabelKey"/>): the conversion that rebuilt the options carried it over.
    /// </summary>
    public override IReadOnlyDictionary<string, string> MapChildrenByClass(CustomPropertyContentV1 from,
        CustomPropertyContentV1 to)
    {
        var ignoreCase = from.IgnoreCase == true;
        return DataSyncChildClassMap.Map(ChildrenOf(from), ChildrenOf(to),
            label => DataSyncLabelKey.Fold(label, ignoreCase));
    }

    /// <summary>Every option, multilevel descendants included.</summary>
    public override int ChildCountOf(CustomPropertyContentV1 content) =>
        content.Choices.Count + content.Tags.Count + CountNodes(content.Nodes);

    /// <summary>Every option that has a uuid (one without cannot be addressed), in list order, nodes pre-order.</summary>
    public override IReadOnlyList<DataSyncChildInfo> ChildrenOf(CustomPropertyContentV1 content)
    {
        var children = new List<DataSyncChildInfo>();
        foreach (var choice in content.Choices)
        {
            if (choice.Uuid is not null)
                children.Add(new DataSyncChildInfo(choice.Uuid, null, new DataSyncDisplayValue(choice.Label, choice.Color)));
        }

        foreach (var tag in content.Tags)
        {
            if (tag.Uuid is not null)
                children.Add(new DataSyncChildInfo(tag.Uuid, null, new DataSyncDisplayValue(tag.Name, tag.Color, tag.Group)));
        }

        AddNodes(content.Nodes, null, []);
        return children;

        void AddNodes(IReadOnlyList<CustomPropertyNodeV1> nodes, string? parentId, IReadOnlyList<string> parentPath)
        {
            foreach (var node in nodes)
            {
                var path = parentPath.Append(node.Label).ToArray();
                if (node.Uuid is not null)
                    children.Add(new DataSyncChildInfo(node.Uuid, parentId,
                        new DataSyncDisplayValue(node.Label, node.Color, Path: path)));
                // A node without a uuid still has addressable children; they keep its parent's id.
                AddNodes(node.Children, node.Uuid ?? parentId, path);
            }
        }
    }

    internal static int CountNodes(IReadOnlyList<CustomPropertyNodeV1> nodes) =>
        nodes.Sum(n => 1 + CountNodes(n.Children));
}
