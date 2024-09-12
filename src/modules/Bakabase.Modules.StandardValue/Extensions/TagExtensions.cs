using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Extensions;

public static class TagExtensions
{
    public static List<TagValue> RemoveEmpty(this IEnumerable<TagValue> tags)
    {
        return tags.Select(x =>
        {
            x.Name = x.Name.Trim();
            x.Group = x.Group?.Trim();
            if (x.Name.IsNullOrEmpty() && x.Group.IsNullOrEmpty())
            {
                return null;
            }

            return x;
        }).OfType<TagValue>().ToList();
    }

    public static void TrimAll(this IEnumerable<TagValue> tags)
    {
        foreach (var x in tags)
        {
            x.Name = x.Name.Trim();
            x.Group = x.Group?.Trim();
        }
    }
}