using Bakabase.Modules.Acquisition.Components;

namespace Bakabase.Modules.Acquisition.Tests;

/// <summary>
/// Deciding that a file which just landed in the downloads folder is the one a particular
/// acquisition was waiting for. It is the only genuinely uncertain judgement in the pipeline, and
/// it moves files when it is confident — so what matters as much as getting it right is refusing to
/// answer when two candidates look alike.
/// </summary>
[TestClass]
public sealed class InboxClaimScoringTests
{
    private static readonly DateTime WaitStart = new(2026, 3, 1, 12, 0, 0);

    private static InboxFile File(string name, int minutesAfterWaitStart = 5) =>
        new(name, WaitStart.AddMinutes(minutesAfterWaitStart));

    private static InboxExpectation Expecting(string? title, string? fileName = null, string? code = null) =>
        new(title, fileName, code, WaitStart);

    [TestMethod]
    public void TheNameTheContentPromisedIsTheStrongestSignal()
    {
        var exact = InboxClaimScorer.Score(File("A Great Work.zip"),
            Expecting("A Great Work", "A Great Work.zip"));

        Assert.IsTrue(exact >= InboxClaimScorer.ConfidentThreshold, $"scored {exact}");
    }

    /// <summary>
    /// Downloads get re-punctuated on the way. Matching words rather than characters is what keeps
    /// "A.Great.Work.2024" recognisable as "A Great Work (2024)".
    /// </summary>
    [TestMethod]
    public void RepunctuatedNamesStillMatch()
    {
        var score = InboxClaimScorer.Score(File("A.Great.Work.2024.rar"),
            Expecting("A Great Work (2024)"));

        Assert.IsTrue(score >= InboxClaimScorer.ConfidentThreshold, $"scored {score}");
    }

    [TestMethod]
    public void AnUnrelatedFileScoresNothingWorthActingOn()
    {
        var score = InboxClaimScorer.Score(File("invoice-2026-03.pdf"), Expecting("A Great Work"));

        Assert.IsTrue(score < InboxClaimScorer.ConfidentThreshold, $"scored {score}");
    }

    /// <summary>Some sites bracket the access code into the file name, and it is nearly unique.</summary>
    [TestMethod]
    public void TheAccessCodeInAFileNameCounts()
    {
        var withCode = InboxClaimScorer.Score(File("[8k2p] something.zip"),
            Expecting("A Great Work", code: "8k2p"));
        var without = InboxClaimScorer.Score(File("something.zip"),
            Expecting("A Great Work", code: "8k2p"));

        Assert.IsTrue(withCode > without);
    }

    /// <summary>
    /// A file that appeared while the run was waiting is likelier than one that was already there —
    /// but only enough to break a tie, never enough to carry a bad name match.
    /// </summary>
    [TestMethod]
    public void RecencyBreaksTiesButDoesNotDecideOnItsOwn()
    {
        var fresh = InboxClaimScorer.Score(File("unrelated.bin", 1), Expecting("A Great Work"));

        Assert.IsTrue(fresh < InboxClaimScorer.ConfidentThreshold,
            "landing at the right moment is not evidence of being the right file");

        var recent = InboxClaimScorer.Score(File("A Great Work.zip", 1), Expecting("A Great Work"));
        var old = InboxClaimScorer.Score(File("A Great Work.zip", -600), Expecting("A Great Work"));

        Assert.IsTrue(recent > old);
    }

    /// <summary>
    /// Two acquisitions of similarly-named things is exactly when a wrong guess is most costly and
    /// least visible, so a near tie is answered by nobody.
    /// </summary>
    [TestMethod]
    public void TwoRunsThatLookEquallyLikelyGetNoAnswer()
    {
        var file = File("A Great Work Vol 2.zip");

        var picked = InboxClaimScorer.BestMatch(file,
        [
            ("first", Expecting("A Great Work Vol 2")),
            ("second", Expecting("A Great Work Vol 2")),
        ]);

        Assert.IsNull(picked);
    }

    [TestMethod]
    public void OneClearWinnerIsClaimed()
    {
        var file = File("A Great Work.zip");

        var picked = InboxClaimScorer.BestMatch(file,
        [
            ("right", Expecting("A Great Work", "A Great Work.zip")),
            ("wrong", Expecting("Something Else Entirely")),
        ]);

        Assert.AreEqual("right", picked);
    }

    [TestMethod]
    public void NothingScoringWellEnoughIsClaimedByNobody()
    {
        var picked = InboxClaimScorer.BestMatch(File("random-download.bin"),
            [("only", Expecting("A Great Work"))]);

        Assert.IsNull(picked);
    }

    [TestMethod]
    public void AnEmptyExpectationNeverMatchesAnything()
    {
        Assert.AreEqual(0, InboxClaimScorer.Score(File("anything.zip", -600), Expecting(null)));
    }
}
