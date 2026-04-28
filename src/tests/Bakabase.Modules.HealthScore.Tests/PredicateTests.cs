using System.Text.Json;
using Bakabase.Modules.HealthScore.Components.Predicates;
using FluentAssertions;

namespace Bakabase.Modules.HealthScore.Tests;

[TestClass]
public class PredicateTests
{
    private static string Json(object o) => JsonSerializer.Serialize(o);

    [TestMethod]
    public void MediaTypeFileCount_GreaterThan_RespectsCount()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("a.mp4", new byte[] { 1 });
        tmp.Touch("b.mkv", new byte[] { 1 });
        tmp.Touch("c.avi", new byte[] { 1 });

        var p = new MediaTypeFileCountPredicate();
        p.Evaluate(tmp.Snapshot(), Json(new MediaTypeFileCountParameters
        {
            MediaType = PredicateMediaType.Video,
            Operator = ComparisonOperator.GreaterThan,
            Count = 2,
        })).Should().BeTrue();

        p.Evaluate(tmp.Snapshot(), Json(new MediaTypeFileCountParameters
        {
            MediaType = PredicateMediaType.Video,
            Operator = ComparisonOperator.LessThan,
            Count = 2,
        })).Should().BeFalse();
    }

    [TestMethod]
    public void MediaTypeFileCount_NoVideos_EqualsZero()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("only.txt");

        new MediaTypeFileCountPredicate().Evaluate(tmp.Snapshot(), Json(new MediaTypeFileCountParameters
        {
            MediaType = PredicateMediaType.Video,
            Operator = ComparisonOperator.Equals,
            Count = 0,
        })).Should().BeTrue();
    }

    [TestMethod]
    public void MediaTypeTotalSize_AggregatesAcrossFiles()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("a.jpg", new byte[100]);
        tmp.Touch("b.png", new byte[200]);

        new MediaTypeTotalSizePredicate().Evaluate(tmp.Snapshot(), Json(new MediaTypeTotalSizeParameters
        {
            MediaType = PredicateMediaType.Image,
            Operator = ComparisonOperator.GreaterThanOrEquals,
            Bytes = 300,
        })).Should().BeTrue();
    }

    [TestMethod]
    public void FileNamePatternCount_RegexMatches()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("subs.srt");
        tmp.Touch("episode.zh.srt");
        tmp.Touch("video.mp4");

        new FileNamePatternCountPredicate().Evaluate(tmp.Snapshot(), Json(new FileNamePatternCountParameters
        {
            Pattern = @"\.srt$",
            Operator = ComparisonOperator.GreaterThanOrEquals,
            Count = 2,
        })).Should().BeTrue();
    }

    [TestMethod]
    public void FileNamePatternCount_BadRegex_ReturnsFalse()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("a.txt");

        new FileNamePatternCountPredicate().Evaluate(tmp.Snapshot(), Json(new FileNamePatternCountParameters
        {
            Pattern = "[unclosed",
            Operator = ComparisonOperator.GreaterThan,
            Count = 0,
        })).Should().BeFalse();
    }

    [TestMethod]
    public void HasCoverImage_DetectsCoverFile()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("Cover.JPG"); // case insensitive
        new HasCoverImagePredicate().Evaluate(tmp.Snapshot(), null).Should().BeTrue();
    }

    [TestMethod]
    public void HasCoverImage_NoCover_ReturnsFalse()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("normal.jpg");
        new HasCoverImagePredicate().Evaluate(tmp.Snapshot(), null).Should().BeFalse();
    }

    [TestMethod]
    public void FileSizeOutOfRange_ZeroByteCaught()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("zero.txt");
        tmp.Touch("normal.txt", new byte[] { 1 });

        new FileSizeOutOfRangePredicate().Evaluate(tmp.Snapshot(), Json(new FileSizeOutOfRangeParameters
        {
            Min = 1, // anything below 1 byte
        })).Should().BeTrue();
    }

    [TestMethod]
    public void FileSizeOutOfRange_NoFilesMatching_ReturnsFalse()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("normal.txt", new byte[] { 1, 2, 3 });

        new FileSizeOutOfRangePredicate().Evaluate(tmp.Snapshot(), Json(new FileSizeOutOfRangeParameters
        {
            Min = 1,
            Max = 1024,
        })).Should().BeFalse();
    }

    [TestMethod]
    public void FileSizeOutOfRange_NeitherBoundSet_ReturnsFalse()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("any.txt", new byte[] { 1 });

        new FileSizeOutOfRangePredicate().Evaluate(tmp.Snapshot(), Json(new FileSizeOutOfRangeParameters()))
            .Should().BeFalse();
    }

    [TestMethod]
    public void RootDirectoryExists_TrueForRealDir()
    {
        using var tmp = TestHelpers.CreateTempDir();
        new RootDirectoryExistsPredicate().Evaluate(tmp.Snapshot(), null).Should().BeTrue();
    }

    [TestMethod]
    public void RootDirectoryExists_FalseForGoneDir()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid());
        var snap = new Bakabase.Modules.HealthScore.Components.ResourceFsSnapshot(bogus);
        new RootDirectoryExistsPredicate().Evaluate(snap, null).Should().BeFalse();
    }
}
