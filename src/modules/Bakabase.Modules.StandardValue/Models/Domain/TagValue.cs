using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Models.Domain;

public record TagValue(string? Group, string Name)
{
    public string? Group { get; set; } = Group.IsNullOrEmpty() ? null : Group;
    public string Name { get; set; } = Name;

    public override string ToString()
    {
        return string.IsNullOrEmpty(Group) ? Name : $"{Group}{Separator}{Name}";
    }

    public const char Separator = ':';

    public static TagValue? TryParse(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var parts = text.Split(Separator).Select(x => x.Trim()).Where(x => x.IsNotEmpty()).ToArray();
        return parts.Length == 1
            ? new TagValue(null, parts[0])
            : new TagValue(parts[0], string.Join(Separator, parts[1..]));
    }

    private sealed class GroupNameEqualityComparer : IEqualityComparer<TagValue>
    {
        public bool Equals(TagValue? x, TagValue? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.Group == y.Group && x.Name == y.Name;
        }

        public int GetHashCode(TagValue obj)
        {
            return HashCode.Combine(obj.Group, obj.Name);
        }
    }

    public static IEqualityComparer<TagValue> GroupNameComparer { get; } = new GroupNameEqualityComparer();
}