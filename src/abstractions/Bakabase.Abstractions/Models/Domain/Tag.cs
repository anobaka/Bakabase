using Bakabase.Abstractions.Components.Tag;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Domain;
public record Tag: ITagBizKey
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Group { get; set; }
    public string? Color { get; set; }
    public int Order { get; set; }

    private sealed class BizEqualityComparer : IEqualityComparer<ITagBizKey>
    {
        public bool Equals(ITagBizKey? x, ITagBizKey? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;
            return x.Name == y.Name && x.Group == y.Group;
        }

        public int GetHashCode(ITagBizKey obj)
        {
            return HashCode.Combine(obj.Name, obj.Group);
        }
    }

    public static IEqualityComparer<ITagBizKey> BizComparer { get; } = new BizEqualityComparer();
}