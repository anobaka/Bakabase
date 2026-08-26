namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// Code handle for a text type whose semantics are bound at a consumption site (cleaning,
/// date parsing, display-name wrappers, ...). A <see cref="Bakabase.Modules.Text.Abstractions.Models.Db.TextType"/> row carrying one of
/// these is a builtin: it cannot be renamed or deleted, though its entries stay editable.
/// Rows without a handle are user-defined and have no semantics beyond being referenced by id.
///
/// Values intentionally match the retired <c>SpecialTextType</c> so the one-shot data migration
/// is a plain lookup and existing localization keys map over mechanically.
/// </summary>
public enum WellKnownTextType
{
    /// <summary>
    /// Regex patterns; wrapped occurrences are stripped during cleaning.
    /// </summary>
    Useless = 1,

    /// <summary>
    /// Left/right delimiter pairs (<c>(</c>/<c>)</c>, <c>[</c>/<c>]</c>, ...).
    /// </summary>
    Wrapper = 3,

    /// <summary>
    /// Replacement mapping applied before comparison: every occurrence of the first value
    /// becomes the second.
    /// </summary>
    Standardization = 4,

    /// <summary>
    /// Regex patterns identifying a volume/chapter marker. Historical rows also carry an
    /// ordinal in the second value which no consumer reads today; the migration preserves it.
    /// </summary>
    Volume = 6,

    /// <summary>
    /// Texts whose surrounding whitespace is collapsed during cleaning.
    /// </summary>
    Trim = 7,

    /// <summary>
    /// <see cref="System.DateTime.TryParseExact(string, string[], System.IFormatProvider, System.Globalization.DateTimeStyles, out System.DateTime)"/>
    /// format strings.
    /// </summary>
    DateTime = 8,

    /// <summary>
    /// Mapping from a regex matching a language marker to the canonical language label.
    /// </summary>
    Language = 9
}
