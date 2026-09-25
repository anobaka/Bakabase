namespace Bakabase.Modules.DataSync.Tests.TestKinds;

/// <summary>One child of a test item: an id (like an option uuid) and a label.</summary>
public sealed record TestChild(string Id, string Label);

/// <summary>
/// Content of the generic test kind <c>testItem</c>:
/// <c>{"children":[{"id":"c1","label":"A"}],"color":"#e5484d","name":"Genre","type":"Tags"}</c>. <c>color</c> is
/// omitted when null or empty, <c>children</c> when there are none, <c>type</c> (the subtype, like a property type)
/// when null. Children keep their list order, and labels may repeat.
/// </summary>
/// <remarks>
/// <see cref="ChildrenLocal"/> ("sync the definition only", §3.6) is set only on PUBLISHED content, which then
/// carries <c>"childrenLocal":true</c> and no children. A device keeps the flag on its side row, never in its local
/// content, as the custom property kind does.
/// </remarks>
public sealed class TestItemContent : IEquatable<TestItemContent>
{
    public TestItemContent(string name, string? color, IEnumerable<TestChild> children, string? type = null,
        bool childrenLocal = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(children);
        Name = name;
        Color = string.IsNullOrEmpty(color) ? null : color;
        Children = children.ToList();
        Type = string.IsNullOrEmpty(type) ? null : type;
        ChildrenLocal = childrenLocal;
    }

    public string Name { get; }
    public string? Color { get; }
    public IReadOnlyList<TestChild> Children { get; }

    /// <summary>The subtype; a peer changing it is a type change (§8.5.6).</summary>
    public string? Type { get; }

    /// <summary>Published content only: "sync the definition only" (§3.6), so no children travel.</summary>
    public bool ChildrenLocal { get; }

    public TestItemContent With(string? name = null, string? color = null, IEnumerable<TestChild>? children = null,
        bool clearColor = false, string? type = null) =>
        new(name ?? Name, clearColor ? null : color ?? Color, children ?? Children, type ?? Type, ChildrenLocal);

    public bool Equals(TestItemContent? other) =>
        other is not null && Name == other.Name && Color == other.Color && Type == other.Type &&
        ChildrenLocal == other.ChildrenLocal && Children.SequenceEqual(other.Children);

    public override bool Equals(object? obj) => obj is TestItemContent other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Color);
        hash.Add(Type);
        hash.Add(ChildrenLocal);
        foreach (var child in Children) hash.Add(child);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{Name}{(Type is null ? "" : ":" + Type)} ({Color ?? "-"}) " +
        (ChildrenLocal ? "[children local]" : $"[{string.Join(", ", Children.Select(c => $"{c.Id}={c.Label}"))}]");
}
