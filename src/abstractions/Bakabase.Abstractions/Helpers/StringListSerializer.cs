namespace Bakabase.Abstractions.Helpers;

/// <summary>
/// Lightweight string list serialization using newline separator.
/// For infrastructure fields (paths, URLs) that don't need StandardValue format.
/// </summary>
public static class StringListSerializer
{
    private const char Separator = '\n';

    public static string? Serialize(List<string>? list)
    {
        if (list == null || list.Count == 0) return null;
        return string.Join(Separator, list);
    }

    public static List<string>? Deserialize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Split(Separator).ToList();
    }
}
