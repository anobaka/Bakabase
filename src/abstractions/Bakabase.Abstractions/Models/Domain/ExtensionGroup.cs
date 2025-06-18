namespace Bakabase.Abstractions.Models.Domain;

public record ExtensionGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public HashSet<string>? Extensions { get; set; }

    private sealed class NameExtensionsEqualityComparer : IEqualityComparer<ExtensionGroup>
    {
        public bool Equals(ExtensionGroup? x, ExtensionGroup? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null) return false;
            if (y is null) return false;
            if (x.Name == y.Name)
            {
                if (x.Extensions?.Count == 0 && y.Extensions?.Count == 0)
                {
                    return true;
                }

                if (x.Extensions?.Count == y.Extensions?.Count)
                {
                    return x.Extensions!.SetEquals(y.Extensions!);
                }
            }

            return false;
        }

        public int GetHashCode(ExtensionGroup obj)
        {
            return HashCode.Combine(obj.Name, obj.Extensions);
        }
    }

    public static IEqualityComparer<ExtensionGroup> BizComparer { get; } = new NameExtensionsEqualityComparer();
}