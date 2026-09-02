using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Text;

/// <summary>
/// Text operations driven by the vocabulary: what is done with the words. Stateless — every method
/// is a pure function of its inputs plus the current vocabulary, which makes the whole surface
/// unit-testable and reusable by workflow nodes, enhancers, downloaders and comparison alike.
/// </summary>
public interface ITextOps
{
    /// <summary>
    /// Normalizes a name for analysis: applies standardization mappings, collapses whitespace,
    /// trims around trim markers and wrappers, then strips wrapped useless words.
    /// </summary>
    Task<string> Clean(string text);

    /// <summary>
    /// Removes wrapped segments whose content matches the given type's entries.
    /// </summary>
    /// <param name="wrappersTypeId">A <see cref="TextTypeShape.DelimiterPair"/> type.</param>
    /// <param name="setTypeId">The type whose entries the wrapped content is matched against.</param>
    Task<string> RemoveWrapped(string text, int wrappersTypeId, int setTypeId, TextMatchMode mode);

    /// <summary>
    /// Removes bare occurrences matching the given type's entries.
    /// </summary>
    Task<string> RemoveTexts(string text, int setTypeId, TextMatchMode mode);

    /// <summary>
    /// Cleans up leftovers of removal: repeated whitespace, empty wrapper pairs, and edge
    /// whitespace or separators.
    /// </summary>
    Task<string> Trim(string text, TextTrimOptions options);

    /// <summary>
    /// Parses using the configured date-time formats, falling back to <see cref="DateTime.TryParse(string, out DateTime)"/>.
    /// </summary>
    Task<DateTime?> TryParseDateTime(string? text);

    /// <inheritdoc cref="TryParseDateTime(string?)"/>
    Task<List<(int Index, DateTime DateTime)>> TryParseDateTime(string[] texts);
}

/// <summary>
/// How an entry is matched against a candidate piece of text.
/// </summary>
public enum TextMatchMode
{
    /// <summary>The candidate equals an entry (case-insensitive).</summary>
    EqualsAny = 1,

    /// <summary>The candidate contains an entry (case-insensitive).</summary>
    ContainsAny = 2,

    /// <summary>An entry, read as a regular expression, matches the candidate.</summary>
    RegexAny = 3
}

/// <param name="CollapseSpaces">Collapse runs of whitespace into a single space.</param>
/// <param name="TrimEnds">Trim whitespace and separator characters from both ends.</param>
/// <param name="RemoveEmptyWrappers">Drop wrapper pairs left holding nothing but whitespace.</param>
public readonly record struct TextTrimOptions(
    bool CollapseSpaces = true,
    bool TrimEnds = true,
    bool RemoveEmptyWrappers = true)
{
    // Spelled out rather than `new()`: a record struct's parameterless constructor zeroes every
    // field, so it would produce all-false instead of the declared defaults.
    public static readonly TextTrimOptions Default = new(true, true, true);
}
