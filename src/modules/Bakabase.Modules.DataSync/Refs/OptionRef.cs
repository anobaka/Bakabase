using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bakabase.Modules.DataSync.Refs;

/// <summary>
/// The only portable reference (v3.1 §3.2, §3.2 here): one option of a custom property, used in its
/// <c>defaultValue</c>. It never leaves its own property, and content never carries a local integer id.
/// <code>
/// {"label":"Action","uuid":"&lt;id&gt;"}                  choice
/// {"group":"Studio","name":"Kyoto","uuid":"&lt;id&gt;"}    tag; "group" omitted only when null
/// {"path":["Asia","Japan"],"uuid":"&lt;id&gt;"}           multilevel node
/// </code>
/// </summary>
public sealed record OptionRef
{
    private OptionRef(string uuid, string? label, string? group, string? name, IReadOnlyList<string>? path)
    {
        Uuid = uuid;
        Label = label;
        Group = group;
        Name = name;
        Path = path;
    }

    public string Uuid { get; }

    /// <summary>Choice: the option's label.</summary>
    public string? Label { get; }

    /// <summary>Tag: its group; null when the tag has none (then the member is omitted).</summary>
    public string? Group { get; }

    /// <summary>Tag: its name.</summary>
    public string? Name { get; }

    /// <summary>Multilevel: the labels from the root down to the node, at least one.</summary>
    public IReadOnlyList<string>? Path { get; }

    public static OptionRef Choice(string uuid, string label)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        ArgumentNullException.ThrowIfNull(label);
        return new OptionRef(uuid, label, null, null, null);
    }

    public static OptionRef Tag(string uuid, string? group, string name)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        ArgumentNullException.ThrowIfNull(name);
        return new OptionRef(uuid, null, group, name, null);
    }

    public static OptionRef Node(string uuid, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0 || path.Any(p => p is null))
            throw new ArgumentException("A node path names at least one label, none of them null.", nameof(path));
        return new OptionRef(uuid, null, null, null, path.ToArray());
    }

    /// <summary>The content form; its members are emitted in any order and sorted by <c>CanonicalJson</c>.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject { ["uuid"] = Uuid };
        if (Label is not null) json["label"] = Label;
        if (Name is not null)
        {
            if (Group is not null) json["group"] = Group;
            json["name"] = Name;
        }

        if (Path is not null) json["path"] = new JsonArray(Path.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        return json;
    }

    /// <summary>
    /// Reads one of the three forms. Never throws: false for anything else, including unknown members and a
    /// mix of forms. Length and character rules are the codec's (v3.1 §6.3).
    /// </summary>
    public static bool TryRead(JsonNode? node, out OptionRef? optionRef)
    {
        optionRef = null;
        try
        {
            if (node is not JsonObject obj || !TryGetString(obj, "uuid", out var uuid)) return false;
            var members = obj.Select(m => m.Key).Where(k => k != "uuid").OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();
            switch (members)
            {
                case ["label"] when TryGetString(obj, "label", out var label):
                    optionRef = Choice(uuid, label);
                    return true;
                case ["name"] when TryGetString(obj, "name", out var name):
                    optionRef = Tag(uuid, null, name);
                    return true;
                case ["group", "name"] when TryGetString(obj, "group", out var group) &&
                                            TryGetString(obj, "name", out var groupedName):
                    optionRef = Tag(uuid, group, groupedName);
                    return true;
                case ["path"] when obj["path"] is JsonArray { Count: > 0 } array:
                    var path = new string[array.Count];
                    for (var i = 0; i < array.Count; i++)
                    {
                        if (array[i] is not JsonValue v || v.GetValueKind() != JsonValueKind.String ||
                            !v.TryGetValue(out string? segment)) return false;
                        path[i] = segment;
                    }

                    optionRef = Node(uuid, path);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception)
        {
            // Duplicate member names in parsed text throw when the object is first read; a ref is then unreadable.
            optionRef = null;
            return false;
        }
    }

    public bool Equals(OptionRef? other) =>
        other is not null && Uuid == other.Uuid && Label == other.Label && Group == other.Group &&
        Name == other.Name && (Path is null ? other.Path is null : other.Path is not null && Path.SequenceEqual(other.Path));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Uuid);
        hash.Add(Label);
        hash.Add(Group);
        hash.Add(Name);
        if (Path is not null)
        {
            foreach (var label in Path) hash.Add(label);
        }

        return hash.ToHashCode();
    }

    private static bool TryGetString(JsonObject obj, string member, out string value)
    {
        value = null!;
        if (obj[member] is not JsonValue v || v.GetValueKind() != JsonValueKind.String ||
            !v.TryGetValue(out string? s)) return false;
        value = s;
        return true;
    }
}
