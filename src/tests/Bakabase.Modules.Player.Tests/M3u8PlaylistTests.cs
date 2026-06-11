using System.Text;
using Bakabase.Modules.Player.Components;
using FluentAssertions;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public class M3u8PlaylistTests
{
    [TestMethod]
    public void Build_StartsWithExtM3uHeader()
    {
        var content = M3u8Playlist.Build([new M3u8Entry(@"C:\videos\a.mp4")]);

        content.Should().StartWith("#EXTM3U\n");
    }

    [TestMethod]
    public void Build_WritesOneExtInfAndPathPerEntry()
    {
        var content = M3u8Playlist.Build(
        [
            new M3u8Entry(@"C:\videos\a.mp4"),
            new M3u8Entry(@"C:\videos\b.mkv", "Custom Title"),
        ]);

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().Equal(
            "#EXTM3U",
            "#EXTINF:-1,a",
            @"C:\videos\a.mp4",
            "#EXTINF:-1,Custom Title",
            @"C:\videos\b.mkv");
    }

    [TestMethod]
    public void Build_DefaultsTitleToFileNameWithoutExtension()
    {
        var content = M3u8Playlist.Build([new M3u8Entry(@"C:\videos\第01话 [1080p].mp4")]);

        content.Should().Contain("#EXTINF:-1,第01话 [1080p]");
    }

    [TestMethod]
    public void Build_SkipsEmptyPaths()
    {
        var content = M3u8Playlist.Build([new M3u8Entry(""), new M3u8Entry(@"C:\a.mp4")]);

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3);
    }

    [TestMethod]
    public void Build_StripsLineBreaksFromTitles()
    {
        var content = M3u8Playlist.Build([new M3u8Entry(@"C:\a.mp4", "line1\r\nline2")]);

        content.Should().Contain("#EXTINF:-1,line1  line2");
    }

    [TestMethod]
    public async Task WriteTempFile_WritesUtf8WithoutBom()
    {
        var dir = CreateTempDir();
        try
        {
            var path = await M3u8Playlist.WriteTempFileAsync(dir,
                [new M3u8Entry(@"C:\videos\日本語タイトル.mp4")], CancellationToken.None);

            path.Should().EndWith(".m3u8");
            var bytes = await File.ReadAllBytesAsync(path);
            bytes.Take(3).Should().NotEqual([(byte)0xEF, (byte)0xBB, (byte)0xBF]);
            Encoding.UTF8.GetString(bytes).Should().Contain("日本語タイトル");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task WriteTempFile_GeneratesUniqueFileNames()
    {
        var dir = CreateTempDir();
        try
        {
            var p1 = await M3u8Playlist.WriteTempFileAsync(dir, [new M3u8Entry(@"C:\a.mp4")],
                CancellationToken.None);
            var p2 = await M3u8Playlist.WriteTempFileAsync(dir, [new M3u8Entry(@"C:\a.mp4")],
                CancellationToken.None);

            p1.Should().NotBe(p2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task SweepOldFiles_RemovesOnlyExpiredGeneratedPlaylists()
    {
        var dir = CreateTempDir();
        try
        {
            var old = await M3u8Playlist.WriteTempFileAsync(dir, [new M3u8Entry(@"C:\a.mp4")],
                CancellationToken.None);
            File.SetLastWriteTime(old, DateTime.Now.AddDays(-2));
            var fresh = await M3u8Playlist.WriteTempFileAsync(dir, [new M3u8Entry(@"C:\a.mp4")],
                CancellationToken.None);
            var foreign = Path.Combine(dir, "user-data.m3u8");
            await File.WriteAllTextAsync(foreign, "#EXTM3U");
            File.SetLastWriteTime(foreign, DateTime.Now.AddDays(-2));

            M3u8Playlist.SweepOldFiles(dir, TimeSpan.FromDays(1));

            File.Exists(old).Should().BeFalse();
            File.Exists(fresh).Should().BeTrue();
            // Files we didn't generate are never touched.
            File.Exists(foreign).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void SweepOldFiles_MissingDirectoryIsIgnored()
    {
        var act = () => M3u8Playlist.SweepOldFiles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            TimeSpan.FromDays(1));

        act.Should().NotThrow();
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bakabase-player-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
