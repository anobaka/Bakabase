using System.Globalization;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Planning;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

public sealed partial class CustomPropertyCodec
{
    /// <summary>
    /// What this device publishes for a local property (§3.5), in this order:
    /// <list type="number">
    /// <item>overlay children (<c>LocalOnlyChildren</c>, <c>HeldChildren</c>) out, a node with its subtree, and
    /// <c>defaultValue</c> refs to them dropped;</item>
    /// <item>with "Sync the definition only" (reference types only), every child and the <c>defaultValue</c> out and
    /// <c>childrenLocal</c> set;</item>
    /// <item>children with an empty uuid or label withheld (§3.3);</item>
    /// <item>the codec's own validated <see cref="DataSyncKindCodec{TContent}.Read"/>: what a reader would drop is left
    /// out, and an entity a reader would hold is returned with <c>Held</c> and no content (for example Invalid,
    /// <c>tooManyChildren</c>, past the per-property option limit).</item>
    /// </list>
    /// Unknown members (step 5) are merged back by <see cref="DataSyncKindCodec{TContent}.WritePublished"/>.
    /// <c>ChildrenWithheld</c> counts the children left out by steps 1, 3 and 4 (nodes with their descendants); with
    /// "Sync the definition only" no child is published by design, and none counts as withheld.
    /// </summary>
    public override DataSyncPublishable Publish(CustomPropertyContentV1 localContent, DataSyncOverlay overlay,
        bool childrenLocal)
    {
        ArgumentNullException.ThrowIfNull(localContent);
        ArgumentNullException.ThrowIfNull(overlay);
        var warnings = new List<DataSyncPlanWarning>();
        var withheld = 0;
        CustomPropertyContentV1 content;
        if (CustomPropertyTypes.IsReference(localContent.Type) && (childrenLocal || localContent.ChildrenLocal))
        {
            content = localContent with { Choices = [], Tags = [], Nodes = [], DefaultValue = [], ChildrenLocal = true };
        }
        else
        {
            // Step 1: overlays. Local-only rules, so no warning.
            var hidden = overlay.HiddenChildIds.ToHashSet(StringComparer.Ordinal);
            content = hidden.Count == 0
                ? localContent
                : RemoveChildren(localContent, (uuid, _) => uuid is not null && hidden.Contains(uuid),
                    (_, _, count) => withheld += count);

            // Step 3: children that cannot be published (§3.3): no uuid, or an empty label or name.
            content = RemoveChildren(content, (uuid, label) => string.IsNullOrEmpty(uuid) || label.Length == 0,
                (uuid, _, count) =>
                {
                    withheld += count;
                    var args = new Dictionary<string, string>
                    {
                        ["reason"] = string.IsNullOrEmpty(uuid) ? DropReasons.Uuid : DropReasons.Label,
                        ["descendants"] = (count - 1).ToString(CultureInfo.InvariantCulture),
                    };
                    if (!string.IsNullOrEmpty(uuid)) args["uuid"] = uuid;
                    warnings.Add(new DataSyncPlanWarning(DataSyncWarningCode.OptionDropped, null, args));
                });
            content = content with { ChildrenLocal = false };
        }

        // Step 4: exactly what a reader accepts.
        var read = Read(Write(content), _limits);
        warnings.AddRange(read.Warnings);
        if (read.Held is { } held)
            return new DataSyncPublishable(null, withheld, warnings.ToArray(), held, read.Errors.FirstOrDefault());
        foreach (var warning in read.Warnings)
        {
            if (warning.Code == DataSyncWarningCode.OptionDropped && warning.Args is { } args)
                withheld += 1 + int.Parse(args["descendants"], CultureInfo.InvariantCulture);
        }

        return new DataSyncPublishable(read.Content, withheld, warnings.ToArray());
    }

    /// <summary>
    /// Removes every option <paramref name="remove"/> selects (a node with its subtree) and the <c>defaultValue</c>
    /// refs whose uuid names a removed option; <paramref name="removed"/> hears each removal with the number of
    /// options it took (a node and its descendants).
    /// </summary>
    private static CustomPropertyContentV1 RemoveChildren(CustomPropertyContentV1 content, Func<string?, string, bool> remove,
        Action<string?, string, int> removed)
    {
        var removedUuids = new HashSet<string>(StringComparer.Ordinal);
        var choices = content.Choices.Where(c => Keep(c.Uuid, c.Label, 1)).ToArray();
        var tags = content.Tags.Where(t => Keep(t.Uuid, t.Name, 1)).ToArray();
        var nodes = Nodes(content.Nodes);
        var defaultValue = content.DefaultValue
            .Where(r => r.Uuid.Length > 0 && !removedUuids.Contains(r.Uuid)).ToArray();
        return content with { Choices = choices, Tags = tags, Nodes = nodes, DefaultValue = defaultValue };

        bool Keep(string? uuid, string label, int count)
        {
            if (!remove(uuid, label)) return true;
            if (uuid is not null) removedUuids.Add(uuid);
            removed(uuid, label, count);
            return false;
        }

        CustomPropertyNodeV1[] Nodes(IReadOnlyList<CustomPropertyNodeV1> level) =>
            level.Where(n =>
                {
                    if (Keep(n.Uuid, n.Label, 1 + CountNodes(n.Children))) return true;
                    CollectUuids(n.Children);
                    return false;
                })
                .Select(n => n.Children.Count == 0 ? n : n with { Children = Nodes(n.Children) })
                .ToArray();

        void CollectUuids(IReadOnlyList<CustomPropertyNodeV1> level)
        {
            foreach (var node in level)
            {
                if (node.Uuid is not null) removedUuids.Add(node.Uuid);
                CollectUuids(node.Children);
            }
        }
    }

    /// <summary>
    /// The comparison form (§3.4) of published (validated) content: id-free and class-folded. One entry per label
    /// class, sorted by key; a class's colour is its representative's; multilevel classes nest their united children;
    /// <c>defaultValue</c> is the sorted class keys its refs resolve to (a node: the key path). A null tag group equals
    /// <c>""</c> and is omitted. With "Sync the definition only" (reference types) children and <c>defaultValue</c> are
    /// left out and <c>"childrenLocal":true</c> is added. Unknown members are added by
    /// <see cref="DataSyncKindCodec{TContent}.ComparisonForm(object, string?, bool, JsonObject?)"/>.
    /// </summary>
    public override JsonObject ComparisonForm(CustomPropertyContentV1 publishedContent, string? orderKey,
        bool childrenLocal)
    {
        ArgumentNullException.ThrowIfNull(publishedContent);
        var content = publishedContent;
        var form = new JsonObject
        {
            [CustomPropertyJson.Name] = content.Name,
            [CustomPropertyJson.Type] = CustomPropertyTypes.NameOf(content.Type)
                                        ?? throw new InvalidOperationException(
                                            $"customProperty: {(int)content.Type} is not a PropertyType."),
        };
        if (content.IgnoreCase is { } ignoreCaseFlag) form[CustomPropertyJson.IgnoreCase] = ignoreCaseFlag;
        if (content.Settings is { } settings) form[CustomPropertyJson.Settings] = CustomPropertyJson.WriteSettings(settings);
        if (orderKey is not null) form["orderKey"] = orderKey;
        if (CustomPropertyTypes.IsReference(content.Type) && (childrenLocal || content.ChildrenLocal))
        {
            form[CustomPropertyJson.ChildrenLocal] = true;
            return form;
        }

        var ignoreCase = content.IgnoreCase == true;
        if (content.Choices.Count > 0)
        {
            form[CustomPropertyJson.Choices] = new JsonArray(ChildClasses.OfChoices(content.Choices, ignoreCase)
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => (JsonNode)WithColor(new JsonObject { ["key"] = c.Key }, c.Color))
                .ToArray());
        }

        if (content.Tags.Count > 0)
        {
            form[CustomPropertyJson.Tags] = new JsonArray(ChildClasses.OfTags(content.Tags, ignoreCase)
                .OrderBy(c => c.Key)
                .Select(c =>
                {
                    var entry = new JsonObject { [CustomPropertyJson.TagName] = c.Key.Name };
                    if (c.Key.Group.Length > 0) entry[CustomPropertyJson.Group] = c.Key.Group;
                    return (JsonNode)WithColor(entry, c.Color);
                })
                .ToArray());
        }

        if (content.Nodes.Count > 0) form[CustomPropertyJson.Nodes] = NodeForms(ChildClasses.OfNodes(content.Nodes, ignoreCase));

        var defaults = content.DefaultValue
            .Select(r => CustomPropertyRefs.Resolve(content, r))
            .OfType<CustomPropertyRefs.Resolved>()
            .Select(r => content.Type == PropertyType.Multilevel
                ? (JsonNode)new JsonArray(r.KeyPath.Select(k => (JsonNode)JsonValue.Create(k)!).ToArray())
                : JsonValue.Create(r.KeyPath[^1])!)
            .Select(n => (Text: CanonicalJson.Serialize(n), Node: n))
            .DistinctBy(n => n.Text, StringComparer.Ordinal)
            .OrderBy(n => n.Text, StringComparer.Ordinal)
            .Select(n => n.Node)
            .ToArray();
        if (defaults.Length > 0) form[CustomPropertyJson.DefaultValue] = new JsonArray(defaults);
        return form;

        static JsonArray NodeForms(IReadOnlyList<NodeClass> classes) => new(classes
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c =>
            {
                var entry = WithColor(new JsonObject { ["key"] = c.Key }, c.Color);
                if (c.Children.Count > 0) entry[CustomPropertyJson.Children] = NodeForms(c.Children);
                return (JsonNode)entry;
            })
            .ToArray());

        static JsonObject WithColor(JsonObject entry, string? color)
        {
            if (!string.IsNullOrEmpty(color)) entry[CustomPropertyJson.Color] = color;
            return entry;
        }
    }
}
