namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// How a type's entries use their two value slots. Drives editing UI, the type picker's
/// filtering (a wrapper-stripping node only accepts <see cref="DelimiterPair"/>) and rendering.
///
/// This is a presentation and validation convention, not a storage constraint:
/// <c>TextEntry.Value2</c> stays nullable for every shape, so historical rows that carry a
/// second value under a <see cref="Values"/> type (Volume ordinals) survive untouched.
/// </summary>
public enum TextTypeShape
{
    /// <summary>
    /// Only the first value is meaningful.
    /// </summary>
    Values = 1,

    /// <summary>
    /// Opening and closing delimiters — the two values bracket a piece of text.
    /// </summary>
    DelimiterPair = 2,

    /// <summary>
    /// A directed mapping — the first value stands for the second.
    /// </summary>
    MappingPair = 3
}
