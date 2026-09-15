using System.Text.RegularExpressions;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>What is known about a file sitting in the inbox.</summary>
/// <param name="FileName">Its name, with extension.</param>
/// <param name="CreatedAt">When it appeared, so a file that showed up while a run was waiting scores higher.</param>
public record InboxFile(string FileName, DateTime CreatedAt);

/// <summary>What a waiting run was expecting.</summary>
/// <param name="Title">What the resource is called.</param>
/// <param name="ExpectedFileName">A name the shared content mentioned, when it did.</param>
/// <param name="AccessCode">The code the drive asked for; some sites put it in the file name.</param>
/// <param name="WaitingSince">When the run started waiting.</param>
public record InboxExpectation(string? Title, string? ExpectedFileName, string? AccessCode,
    DateTime WaitingSince);

/// <summary>
/// How well a file that turned up in the inbox matches what a run is waiting for.
/// <para>
/// A pure function on purpose. Deciding "this download is the one that run wanted" is the only
/// genuinely uncertain judgement in the whole pipeline, and it moves files around when it is
/// confident. Keeping it free of the filesystem and the database is what makes it something a table
/// of cases can pin down.
/// </para>
/// </summary>
public static class InboxClaimScorer
{
    /// <summary>A score at or above this, held alone, is claimed without asking.</summary>
    public const int ConfidentThreshold = 60;

    /// <summary>
    /// Two candidates within this of each other are treated as a tie, however high they scored:
    /// two files that look equally like the answer mean the guess is not worth making.
    /// </summary>
    public const int TieMargin = 15;

    private static readonly Regex Tokens = new(@"[a-z0-9]+", RegexOptions.Compiled);

    /// <summary>0 to 100. Zero means nothing about the file suggests it belongs to this run.</summary>
    public static int Score(InboxFile file, InboxExpectation expectation)
    {
        var score = 0;

        var name = Path.GetFileNameWithoutExtension(file.FileName);

        // The strongest signal there is: the content said what the file would be called.
        if (!string.IsNullOrWhiteSpace(expectation.ExpectedFileName))
        {
            var expected = Path.GetFileNameWithoutExtension(expectation.ExpectedFileName);

            if (string.Equals(name, expected, StringComparison.OrdinalIgnoreCase)) score += 60;
            else if (Overlap(name, expected) is var o and > 0) score += (int) (40 * o);
        }

        // Sites often bracket the access code into the file name, and it is nearly unique.
        if (!string.IsNullOrWhiteSpace(expectation.AccessCode) &&
            name.Contains(expectation.AccessCode, StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        // Weighted so that every word of the title appearing in the file name reaches the
        // threshold on its own. Usually the title is all that is known, and a complete word match
        // against it is about as good as evidence gets short of the content naming the file.
        if (!string.IsNullOrWhiteSpace(expectation.Title))
        {
            score += (int) (60 * Overlap(name, expectation.Title));
        }

        // A file that appeared while the run was waiting is likelier than one that was already
        // there. Small on its own — it only breaks ties between similar names.
        var since = file.CreatedAt - expectation.WaitingSince;

        if (since >= TimeSpan.Zero)
        {
            score += since < TimeSpan.FromMinutes(30) ? 10 : since < TimeSpan.FromHours(6) ? 5 : 0;
        }

        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Which run a newly-arrived file belongs to, if the answer is obvious. Null when nothing scored
    /// well enough, or when two candidates are too close to separate — both mean "ask someone".
    /// </summary>
    public static TRun? BestMatch<TRun>(InboxFile file, IReadOnlyList<(TRun Run, InboxExpectation Expectation)> waiting)
        where TRun : class
    {
        if (waiting.Count == 0) return null;

        var scored = waiting
            .Select(w => (w.Run, Score: Score(file, w.Expectation)))
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scored[0].Score < ConfidentThreshold) return null;
        if (scored.Count > 1 && scored[0].Score - scored[1].Score < TieMargin) return null;

        return scored[0].Run;
    }

    /// <summary>
    /// Share of the expectation's words that appear in the file's name, 0 to 1. Word-level rather
    /// than character-level because downloads get re-punctuated on the way ("A.Work.2024" against
    /// "A Work (2024)") while the words survive.
    /// </summary>
    private static double Overlap(string fileName, string expected)
    {
        var wanted = Tokenize(expected);

        if (wanted.Count == 0) return 0;

        var have = Tokenize(fileName);

        return (double) wanted.Count(have.Contains) / wanted.Count;
    }

    private static HashSet<string> Tokenize(string text) =>
        Tokens.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            // One- and two-character fragments are mostly noise from splitting on punctuation.
            .Where(t => t.Length > 2)
            .ToHashSet();
}
