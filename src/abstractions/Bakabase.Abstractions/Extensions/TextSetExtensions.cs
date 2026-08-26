using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Extensions;

public static class TextSetExtensions
{
    /// <summary>
    /// Pairs as a lookup from first to second value. Later duplicates of a first value are
    /// dropped, matching how the vocabulary has always been consumed.
    /// </summary>
    public static Dictionary<string, string> ToPairMap(this TextSet set) =>
        set.Pairs.GroupBy(p => p.Value1).ToDictionary(g => g.Key, g => g.First().Value2);

    /// <summary>
    /// Pairs as (Left, Right) tuples — the shape display-name rendering expects for wrappers.
    /// </summary>
    public static (string Left, string Right)[] ToLeftRightPairs(this TextSet set) =>
        set.Pairs.Select(p => (Left: p.Value1, Right: p.Value2)).ToArray();
}
