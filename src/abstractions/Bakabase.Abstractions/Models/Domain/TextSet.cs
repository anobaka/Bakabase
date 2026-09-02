using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// A resolved text type: its entries in the form its shape implies. This is the single "read the
/// words" surface consumers use, so none of them touch entry rows directly.
/// </summary>
public record TextSet
{
    public int TypeId { get; set; }

    public TextTypeShape Shape { get; set; }

    /// <summary>
    /// Every entry's first value, in insertion order. Populated for all shapes — for pair shapes
    /// it is the left/source side.
    /// </summary>
    public IReadOnlyList<string> Values { get; set; } = [];

    /// <summary>
    /// Entries of a pair-shaped type whose second value is present. Empty for
    /// <see cref="TextTypeShape.Values"/> types, and skips pair rows missing their second value
    /// rather than materializing a half pair.
    /// </summary>
    public IReadOnlyList<TextPair> Pairs { get; set; } = [];
}

/// <param name="Value1">Opening delimiter, or the source of a mapping.</param>
/// <param name="Value2">Closing delimiter, or the target of a mapping.</param>
public readonly record struct TextPair(string Value1, string Value2);
