using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Extensions;

public static class TagExtensions
{
    /// <summary>
    /// Returns trimmed copies of the tags with fully-empty entries removed.
    /// TagValue is immutable — the input sequence is never modified.
    /// </summary>
    public static List<TagValue> RemoveEmpty(this IEnumerable<TagValue> tags)
    {
        return tags
            .Select(x => new TagValue(x.Group?.Trim(), x.Name.Trim()))
            .Where(x => !(x.Name.IsNullOrEmpty() && x.Group.IsNullOrEmpty()))
            .ToList();
    }

    /// <summary>
    /// Returns trimmed copies of the tags.
    /// TagValue is immutable — the input sequence is never modified.
    /// </summary>
    public static List<TagValue> Trimmed(this IEnumerable<TagValue> tags)
    {
        return tags.Select(x => new TagValue(x.Group?.Trim(), x.Name.Trim())).ToList();
    }
}
