using System.Text;

namespace Bakabase.Abstractions.Components.Identity;

/// <summary>
/// How alike two titles are, for deciding whether two resources might be the same work.
/// <para>
/// This never decides anything on its own: a score above the threshold makes a suggestion the user
/// confirms, never a merge. That is the whole design — a wrong automatic merge silently loses a
/// resource's history, while a wrong suggestion costs one click to dismiss.
/// </para>
/// </summary>
public static class TitleSimilarity
{
    /// <summary>
    /// Above this, two titles are alike enough to be worth asking about. Deliberately high: the list
    /// of things to confirm is only useful while everything on it is plausible.
    /// </summary>
    public const double SuggestionThreshold = 0.82;

    /// <summary>
    /// Below this many characters a title has too few bigrams for the score to mean anything —
    /// "AB" and "AC" would read as half the same work.
    /// </summary>
    private const int MinComparableLength = 4;

    /// <summary>
    /// Strips a title down to what two spellings of the same name have in common: compatibility-
    /// folded (so full-width and half-width Japanese agree), lower-cased, and with everything that
    /// is not a letter or a digit dropped. Punctuation and spacing carry no identity —
    /// "Fate/stay night" and "Fate stay night" are one work.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var folded = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(folded.Length);

        foreach (var c in folded)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// How alike two titles are, from 0 to 1, as the Sørensen–Dice coefficient over character
    /// bigrams.
    /// <para>
    /// Bigrams rather than words because half the titles here have no spaces in them: a Japanese
    /// title is one run of characters, and any word-based measure would score every pair of them
    /// either 1 or 0.
    /// </para>
    /// </summary>
    public static double Score(string? a, string? b)
    {
        var left = Normalize(a);
        var right = Normalize(b);

        if (left.Length == 0 || right.Length == 0) return 0;
        if (left == right) return 1;
        if (left.Length < MinComparableLength || right.Length < MinComparableLength) return 0;

        var leftGrams = Bigrams(left);
        var rightGrams = Bigrams(right);

        var shared = 0;

        foreach (var (gram, count) in leftGrams)
        {
            if (rightGrams.TryGetValue(gram, out var otherCount))
            {
                shared += Math.Min(count, otherCount);
            }
        }

        var total = left.Length - 1 + (right.Length - 1);

        return total == 0 ? 0 : 2.0 * shared / total;
    }

    /// <summary>
    /// Whether these two look like different numbered parts of the same thing — volume 1 and volume
    /// 2, or RJ01 and RJ02.
    /// <para>
    /// This is the one place where a high score is systematically wrong: a series' volumes share
    /// nearly every character and differ in exactly the part that says which one it is. Refusing any
    /// pair whose numbers disagree costs a few real matches and prevents the failure that would make
    /// the whole feature untrustworthy.
    /// </para>
    /// </summary>
    public static bool LooksLikeADifferentInstalment(string? a, string? b)
    {
        var left = DigitRuns(Normalize(a));
        var right = DigitRuns(Normalize(b));

        return !left.SequenceEqual(right);
    }

    /// <summary>
    /// Alike enough to ask about: over the threshold, and not two parts of one series.
    /// </summary>
    public static bool IsWorthConfirming(string? a, string? b, out double score)
    {
        score = Score(a, b);

        return score >= SuggestionThreshold && !LooksLikeADifferentInstalment(a, b);
    }

    private static Dictionary<string, int> Bigrams(string text)
    {
        var grams = new Dictionary<string, int>(text.Length);

        for (var i = 0; i < text.Length - 1; i++)
        {
            var gram = text.Substring(i, 2);

            grams[gram] = grams.GetValueOrDefault(gram) + 1;
        }

        return grams;
    }

    /// <summary>The numbers in a title, in the order they appear, with leading zeroes ignored.</summary>
    private static List<string> DigitRuns(string normalized)
    {
        var runs = new List<string>();
        var current = new StringBuilder();

        foreach (var c in normalized)
        {
            if (char.IsDigit(c))
            {
                current.Append(c);

                continue;
            }

            if (current.Length > 0)
            {
                runs.Add(Trimmed(current));
                current.Clear();
            }
        }

        if (current.Length > 0) runs.Add(Trimmed(current));

        return runs;

        static string Trimmed(StringBuilder builder) =>
            builder.ToString().TrimStart('0') is {Length: > 0} trimmed
                ? trimmed
                : "0";
    }
}
