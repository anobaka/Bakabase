using Bakabase.InsideWorld.Models.Constants;
using FluentAssertions;

namespace Bakabase.Modules.HealthScore.Tests;

[TestClass]
public class ResourceFsSnapshotTests
{
    [TestMethod]
    public void RootExists_WhenDirectoryPresent_ReturnsTrue()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Snapshot().RootExists.Should().BeTrue();
    }

    [TestMethod]
    public void RootExists_WhenMissing_ReturnsFalse()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "this-does-not-exist-" + Guid.NewGuid());
        new Bakabase.Modules.HealthScore.Components.ResourceFsSnapshot(bogus).RootExists.Should().BeFalse();
    }

    [TestMethod]
    public void ByMediaType_ClassifiesExtensions()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("a.mp4", new byte[] { 1, 2, 3 });
        tmp.Touch("b.jpg", new byte[] { 1 });
        tmp.Touch("nested/c.flac", new byte[] { 1, 2 });

        var snap = tmp.Snapshot();
        snap.FilesOf(MediaType.Video).Count().Should().Be(1);
        snap.FilesOf(MediaType.Image).Count().Should().Be(1);
        snap.FilesOf(MediaType.Audio).Count().Should().Be(1);
    }

    [TestMethod]
    public void ZeroByteFiles_DetectsEmpty()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("a.txt"); // zero bytes
        tmp.Touch("b.txt", new byte[] { 1, 2 });

        var snap = tmp.Snapshot();
        snap.ZeroByteFiles.Count.Should().Be(1);
        snap.ZeroByteFiles[0].Name.Should().Be("a.txt");
    }

    [TestMethod]
    public void HasCoverImage_DetectsByName()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("cover.jpg", new byte[] { 1 });
        tmp.Touch("video.mp4", new byte[] { 1 });
        tmp.Snapshot().HasCoverImage.Should().BeTrue();
    }

    [TestMethod]
    public void HasCoverImage_DetectsByReservedCoverPath()
    {
        using var tmp = TestHelpers.CreateTempDir();
        var coverPath = tmp.Touch("nested/foo.jpg", new byte[] { 1 });
        // Filename "foo.jpg" is not a known cover stem; only Reserved.Cover hint should pick it up.
        tmp.Snapshot(coverPaths: new[] { coverPath }).HasCoverImage.Should().BeTrue();
    }

    [TestMethod]
    public void HasCoverImage_IsFalseWhenNoMatch()
    {
        using var tmp = TestHelpers.CreateTempDir();
        tmp.Touch("video.mp4", new byte[] { 1 });
        tmp.Touch("notcover.jpg", new byte[] { 1 });
        tmp.Snapshot().HasCoverImage.Should().BeFalse();
    }
}
