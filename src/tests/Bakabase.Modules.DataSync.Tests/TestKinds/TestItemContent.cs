namespace Bakabase.Modules.DataSync.Tests.TestKinds;

/// <summary>One child of a test item: an id (like an option uuid) and a label.</summary>
public sealed record TestChild(string Id, string Label);

/// <summary>
/// Content of the generic test kind <c>testItem</c>:
/// <c>{"children":[{"id":"c1","label":"A"}],"color":"#e5484d","name":"Genre"}</c>. <c>color</c> is omitted when
/// null or empty, <c>children</c> when there are none. Children keep their list order, and labels may repeat.
/// </summary>
public sealed class TestItemContent : IEquatable<TestItemContent>
{
    public TestItemContent(string name, string? color, IEnumerable<TestChild> children)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(children);
        Name = name;
        Color = string.IsNullOrEmpty(color) ? null : color;
        Children = children.ToList();
    }

    public string Name { get; }
    public string? Color { get; }
    public IReadOnlyList<TestChild> Children { get; }

    public TestItemContent With(string? name = null, string? color = null, IEnumerable<TestChild>? children = null,
        bool clearColor = false) =>
        new(name ?? Name, clearColor ? null : color ?? Color, children ?? Children);

    public bool Equals(TestItemContent? other) =>
        other is not null && Name == other.Name && Color == other.Color && Children.SequenceEqual(other.Children);

    public override bool Equals(object? obj) => obj is TestItemContent other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Color);
        foreach (var child in Children) hash.Add(child);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{Name} ({Color ?? "-"}) [{string.Join(", ", Children.Select(c => $"{c.Id}={c.Label}"))}]";
}
