using Bakabase.Abstractions.Components.Identity;

namespace Bakabase.Tests;

/// <summary>
/// How alike two titles have to be before it is worth asking whether they name one work.
/// <para>
/// The measure exists to fill a list a person reads, so the failure that matters is not a missed
/// match — it is a list full of pairs that are obviously different, which nobody then reads.
/// </para>
/// </summary>
[TestClass]
public sealed class TitleSimilarityTests
{
    /// <summary>
    /// Punctuation and spacing carry no identity. These are the same name written by two people.
    /// </summary>
    [TestMethod]
    public void SpellingsOfOneNameAreTheSameName()
    {
        foreach (var (a, b) in new[]
                 {
                     ("Fate/stay night", "Fate stay night"),
                     ("Steins;Gate", "Steins Gate"),
                     ("ＡＩＲ", "AIR"),
                     ("Clannad ", "clannad"),
                 })
        {
            Assert.AreEqual(1, TitleSimilarity.Score(a, b), $"'{a}' and '{b}'");
        }
    }

    [TestMethod]
    public void AMissingArticleIsStillTheSameWork()
    {
        Assert.IsTrue(
            TitleSimilarity.IsWorthConfirming("The Melancholy of Haruhi Suzumiya",
                "Melancholy of Haruhi Suzumiya", out var score),
            $"scored {score}");
    }

    /// <summary>
    /// The pair the whole feature would be discredited by. Volumes of one series share nearly every
    /// character and differ in exactly the part that says which one they are, so they score higher
    /// than most real matches.
    /// </summary>
    [TestMethod]
    public void NumberedPartsOfOneSeriesAreNotOneWork()
    {
        Assert.IsTrue(TitleSimilarity.Score("Higurashi Kai Chapter 1", "Higurashi Kai Chapter 2") > 0.9,
            "they really do look alike, which is the problem");

        Assert.IsFalse(
            TitleSimilarity.IsWorthConfirming("Higurashi Kai Chapter 1", "Higurashi Kai Chapter 2", out _));
    }

    /// <summary>A number appearing on only one side says the same thing: they are different parts.</summary>
    [TestMethod]
    public void ANumberOnOneSideOnlyIsAlsoADifferentPart()
    {
        Assert.IsFalse(TitleSimilarity.IsWorthConfirming("Some Long Work Name", "Some Long Work Name 2", out _));
    }

    /// <summary>Leading zeroes are how the same volume gets written twice.</summary>
    [TestMethod]
    public void TheSameNumberWrittenTwoWaysIsTheSameNumber()
    {
        Assert.IsFalse(TitleSimilarity.LooksLikeADifferentInstalment("Volume 03", "Volume 3"));
        Assert.IsTrue(TitleSimilarity.LooksLikeADifferentInstalment("Volume 03", "Volume 30"));
    }

    [TestMethod]
    public void DifferentWorksAreNotWorthAskingAbout()
    {
        foreach (var (a, b) in new[]
                 {
                     ("Higurashi no Naku Koro ni", "Umineko no Naku Koro ni"),
                     ("Little Busters", "Angel Beats"),
                     ("かのじょの流儀", "ぼくの夏休み"),
                 })
        {
            Assert.IsFalse(TitleSimilarity.IsWorthConfirming(a, b, out var score), $"'{a}' vs '{b}' scored {score}");
        }
    }

    /// <summary>
    /// Two or three characters have too few bigrams for a score to mean anything — every short name
    /// would look like every other one.
    /// </summary>
    [TestMethod]
    public void VeryShortNamesAreNotCompared()
    {
        Assert.AreEqual(0, TitleSimilarity.Score("AIR", "AIZ"));
        Assert.AreEqual(1, TitleSimilarity.Score("AIR", "air"), "being the same name is still the same name");
    }

    [TestMethod]
    public void NothingIsNotAMatchForAnything()
    {
        Assert.AreEqual(0, TitleSimilarity.Score(null, "Clannad"));
        Assert.AreEqual(0, TitleSimilarity.Score("   ", "Clannad"));
        Assert.AreEqual(0, TitleSimilarity.Score("!!!", "Clannad"), "punctuation normalizes away to nothing");
    }
}
