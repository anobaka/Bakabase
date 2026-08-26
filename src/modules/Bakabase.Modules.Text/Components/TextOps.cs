using System.Globalization;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Text;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.Text.Components;

/// <summary>
/// Vocabulary-driven text operations. Also serves as the app's
/// <see cref="ICustomDateTimeParser"/>, which is how StandardValue conversions pick up the
/// user-configured date formats.
/// </summary>
public class TextOps(ITextVocabularyService vocabulary) : ITextOps, ICustomDateTimeParser
{
    private static readonly Regex RepeatedWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly TimeSpan UserRegexTimeout = TimeSpan.FromSeconds(1);

    public async Task<string> Clean(string text)
    {
        // Standardize
        var standardization = await vocabulary.ResolveSet(WellKnownTextType.Standardization);
        text = standardization.Pairs.Aggregate(text, (current, p) => current.Replace(p.Value1, p.Value2));

        // Remove unnecessary spaces.
        text = RepeatedWhitespaceRegex.Replace(text, " ").Trim();

        var wrappers = await vocabulary.ResolveSet(WellKnownTextType.Wrapper);

        // Trim: drop the whitespace around trim markers and around both sides of every wrapper.
        var trimSet = await vocabulary.ResolveSet(WellKnownTextType.Trim);
        var trimFlags = trimSet.Values.ToList();
        trimFlags.AddRange(wrappers.Pairs.Select(p => p.Value1));
        trimFlags.AddRange(wrappers.Pairs.Select(p => p.Value2));
        text = trimFlags.Aggregate(text,
            (current, flag) => Regex.Replace(current, $@"\s*{Regex.Escape(flag)}\s*", flag));

        // Useless words, but only where they are wrapped.
        var useless = await vocabulary.ResolveSet(WellKnownTextType.Useless);
        text = useless.Values.Aggregate(text, (current1, word) => wrappers.Pairs.Aggregate(current1,
            (current, wrapper) =>
                StringHelpers.BuildRegexWithWrapper(wrapper.Value1, wrapper.Value2, word)
                    .Replace(current, string.Empty)));

        return text;
    }

    public async Task<string> RemoveWrapped(string text, int wrappersTypeId, int setTypeId, TextMatchMode mode)
    {
        var wrappers = await vocabulary.ResolveSet(wrappersTypeId);
        if (wrappers.Shape != TextTypeShape.DelimiterPair)
        {
            throw new InvalidOperationException(
                $"Text type [{wrappersTypeId}] is not a delimiter pair type and cannot supply wrappers.");
        }

        var set = await vocabulary.ResolveSet(setTypeId);
        if (set.Values.Count == 0 || wrappers.Pairs.Count == 0)
        {
            return text;
        }

        foreach (var wrapper in wrappers.Pairs)
        {
            var left = Regex.Escape(wrapper.Value1);
            var right = Regex.Escape(wrapper.Value2);
            // Non-greedy so adjacent wrapped segments are matched one by one rather than as a run.
            var wrappedRegex = new Regex($"{left}(?<content>.*?){right}", RegexOptions.None, UserRegexTimeout);
            text = wrappedRegex.Replace(text,
                m => Matches(m.Groups["content"].Value, set.Values, mode) ? string.Empty : m.Value);
        }

        return text;
    }

    public async Task<string> RemoveTexts(string text, int setTypeId, TextMatchMode mode)
    {
        var set = await vocabulary.ResolveSet(setTypeId);
        foreach (var value in set.Values)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            text = mode switch
            {
                TextMatchMode.RegexAny => new Regex(value, RegexOptions.None, UserRegexTimeout)
                    .Replace(text, string.Empty),
                // Equality against a fragment of a longer string is the same operation as
                // containment; both remove every occurrence.
                _ => Regex.Replace(text, Regex.Escape(value), string.Empty, RegexOptions.IgnoreCase)
            };
        }

        return text;
    }

    public async Task<string> Trim(string text, TextTrimOptions options)
    {
        if (options.RemoveEmptyWrappers)
        {
            var wrappers = await vocabulary.ResolveSet(WellKnownTextType.Wrapper);
            foreach (var wrapper in wrappers.Pairs)
            {
                var emptyRegex = new Regex($"{Regex.Escape(wrapper.Value1)}\\s*{Regex.Escape(wrapper.Value2)}");
                text = emptyRegex.Replace(text, string.Empty);
            }
        }

        if (options.CollapseSpaces)
        {
            text = RepeatedWhitespaceRegex.Replace(text, " ");
        }

        if (options.TrimEnds)
        {
            text = text.Trim().Trim('-', '_', '.', ',', '、', ' ').Trim();
        }

        return text;
    }

    public async Task<DateTime?> TryParseDateTime(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var r = await TryParseDateTime([text]);
        return r.Count > 0 ? r[0].DateTime : null;
    }

    public async Task<List<(int Index, DateTime DateTime)>> TryParseDateTime(string[] texts)
    {
        var list = new List<(int Index, DateTime DateTime)>();
        if (texts.Length == 0)
        {
            return list;
        }

        var set = await vocabulary.ResolveSet(WellKnownTextType.DateTime);
        var formats = set.Values.Distinct().ToArray();
        for (var i = 0; i < texts.Length; i++)
        {
            if (formats.Length > 0 && DateTime.TryParseExact(texts[i], formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var dt))
            {
                list.Add((i, dt));
            }
            else if (DateTime.TryParse(texts[i], out var fallbackDt))
            {
                list.Add((i, fallbackDt));
            }
        }

        return list;
    }

    private static bool Matches(string candidate, IReadOnlyList<string> values, TextMatchMode mode) =>
        mode switch
        {
            TextMatchMode.EqualsAny => values.Any(v => string.Equals(candidate, v, StringComparison.OrdinalIgnoreCase)),
            TextMatchMode.ContainsAny => values.Any(v =>
                !string.IsNullOrEmpty(v) && candidate.Contains(v, StringComparison.OrdinalIgnoreCase)),
            TextMatchMode.RegexAny => values.Any(v =>
                !string.IsNullOrEmpty(v) && Regex.IsMatch(candidate, v, RegexOptions.None, UserRegexTimeout)),
            _ => false
        };

    Task<DateTime?> ICustomDateTimeParser.TryToParseDateTime(string? str) => TryParseDateTime(str);
}
