namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

/// <summary>Which child list of a custom property a <see cref="ChildMerge3"/> merges.</summary>
internal enum ChildListKind { Choices, Tags, Nodes }

/// <summary>
/// One option of one side of a merge, in a mutable tree: a choice or a tag is a root without children, a multilevel
/// node keeps its subtree. <see cref="Label"/> is a choice's or a node's label and a tag's name.
/// </summary>
internal sealed class ChildNode(string? uuid, string label, string? group, string? color)
{
    /// <summary>Set once by a merge that stores one of its options in a local option without an id (AdoptTwins).</summary>
    public string? Uuid { get; set; } = uuid;
    public string Label { get; set; } = label;

    /// <summary>A tag's group, exactly as the content holds it (<c>null</c> and <c>""</c> kept apart).</summary>
    public string? Group { get; set; } = group;

    public string? Color { get; set; } = color;
    public List<ChildNode> Children { get; } = [];
    public ChildNode? Parent { get; set; }

    /// <summary>The parent it was read with: the local structure before the merge moved anything.</summary>
    public ChildNode? OriginalParent { get; set; }

    /// <summary>Pre-order position in the tree it was read from; local order.</summary>
    public int Seq { get; set; }

    // ---- local side only ------------------------------------------------------------------------

    /// <summary>Published by this device (§3.5): not an overlay child, not withheld, not dropped by the reader.</summary>
    public bool Visible { get; set; }

    /// <summary>A <c>LocalOnlyChildren</c> or <c>HeldChildren</c> id (§3.6): invisible to merging, never touched.</summary>
    public bool Overlay { get; set; }

    /// <summary>A <c>HeldChildren</c> id; only a hold is ever released.</summary>
    public bool Held { get; set; }

    /// <summary>Its label class before the merge; visible nodes only.</summary>
    public ChildClass? Class { get; set; }

    /// <summary>The peer class this node is the counterpart of (claimed by id or by key).</summary>
    public PeerClass? Owner { get; set; }

    /// <summary>The group of <see cref="Owner"/> this node belongs to.</summary>
    public ChildGroup? OwnerGroup { get; set; }

    /// <summary>The peer class this node was created for (an add or an edit-wins restore).</summary>
    public PeerClass? CreatedFor { get; set; }

    /// <summary>The part of <see cref="CreatedFor"/> it was created for, when it is not the class's own add.</summary>
    public SplitPart? Part { get; set; }

    public bool Removed { get; set; }

    /// <summary>Held by this merge (the peer deleted it and it is in use here).</summary>
    public bool HeldHere { get; set; }

    public static List<ChildNode> Of(CustomPropertyContentV1 content, ChildListKind kind) => kind switch
    {
        ChildListKind.Choices => content.Choices.Select(c => new ChildNode(c.Uuid, c.Label, null, c.Color)).ToList(),
        ChildListKind.Tags => content.Tags.Select(t => new ChildNode(t.Uuid, t.Name, t.Group, t.Color)).ToList(),
        _ => OfNodes(content.Nodes, null),
    };

    public static IReadOnlyList<CustomPropertyChoiceV1> ToChoices(IEnumerable<ChildNode> roots) =>
        roots.Select(n => new CustomPropertyChoiceV1(n.Uuid, n.Label, n.Color)).ToArray();

    public static IReadOnlyList<CustomPropertyTagV1> ToTags(IEnumerable<ChildNode> roots) =>
        roots.Select(n => new CustomPropertyTagV1(n.Uuid, n.Group, n.Label, n.Color)).ToArray();

    public static IReadOnlyList<CustomPropertyNodeV1> ToNodes(IEnumerable<ChildNode> roots) =>
        roots.Select(n => new CustomPropertyNodeV1(n.Uuid, n.Label, n.Color) { Children = ToNodes(n.Children) })
            .ToArray();

    /// <summary>The node and its subtree, pre-order.</summary>
    public IEnumerable<ChildNode> SelfAndDescendants()
    {
        var stack = new Stack<ChildNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
    }

    /// <summary>Labels from the root down to this node.</summary>
    public IReadOnlyList<string> Path()
    {
        var path = new List<string>();
        for (var node = this; node is not null; node = node.Parent) path.Add(node.Label);
        path.Reverse();
        return path;
    }

    /// <summary>True when this node is <paramref name="ancestor"/> or lies below it.</summary>
    public bool IsWithin(ChildNode ancestor)
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, ancestor)) return true;
        }

        return false;
    }

    public static void PreOrder(IEnumerable<ChildNode> roots, Action<ChildNode> visit)
    {
        foreach (var root in roots)
        {
            foreach (var node in root.SelfAndDescendants()) visit(node);
        }
    }

    private static List<ChildNode> OfNodes(IReadOnlyList<CustomPropertyNodeV1> nodes, ChildNode? parent) =>
        nodes.Select(n =>
        {
            var node = new ChildNode(n.Uuid, n.Label, null, n.Color) { Parent = parent };
            node.Children.AddRange(OfNodes(n.Children, node));
            return node;
        }).ToList();
}

/// <summary>
/// A label class (§3.4) over <see cref="ChildNode"/>s: sibling nodes with equal class keys. A multilevel class's
/// children are the classes of every member's children, concatenated in member order (<see cref="ChildClasses"/>).
/// </summary>
internal sealed class ChildClass(string key)
{
    public string Key { get; } = key;
    public List<ChildNode> Members { get; } = [];
    public List<ChildClass> Children { get; } = [];

    /// <summary>The first member in that side's order: the one a fresh <c>AddRange</c> keeps (F72).</summary>
    public ChildNode Rep => Members[0];

    public IEnumerable<ChildClass> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var descendant in child.SelfAndDescendants()) yield return descendant;
        }
    }
}
