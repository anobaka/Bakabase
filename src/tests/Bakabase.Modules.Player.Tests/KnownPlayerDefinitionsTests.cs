using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Player.Components;
using FluentAssertions;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public class KnownPlayerDefinitionsTests
{
    [TestMethod]
    public void Catalog_IdsAreDistinct()
    {
        KnownPlayerDefinitions.All.Select(d => d.Id).Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void Catalog_EveryPlayerHasABatchCapability()
    {
        foreach (var definition in KnownPlayerDefinitions.All)
        {
            definition.Capabilities.Should().NotBe(BatchPlayCapability.None,
                $"{definition.Id} must support at least one batch launch method");
            definition.ExecutableNames.Should().NotBeEmpty();
        }
    }

    [TestMethod]
    [DataRow(@"C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe", "PotPlayer")]
    [DataRow(@"D:\tools\potplayer64.EXE", "PotPlayer")]
    [DataRow(@"C:\Program Files\VideoLAN\VLC\vlc.exe", "Vlc")]
    [DataRow("/usr/bin/vlc", "Vlc")]
    [DataRow(@"C:\apps\mpv\mpv.exe", "Mpv")]
    [DataRow(@"C:\Program Files\MPC-HC\mpc-hc64.exe", "MpcHc")]
    public void MatchByExecutable_RecognizesKnownExecutables(string path, string expectedId)
    {
        KnownPlayerDefinitions.MatchByExecutable(path)!.Id.Should().Be(expectedId);
    }

    [TestMethod]
    [DataRow(@"C:\Program Files\SomePlayer\someplayer.exe")]
    [DataRow("")]
    [DataRow(null)]
    public void MatchByExecutable_UnknownOrEmpty_ReturnsNull(string? path)
    {
        KnownPlayerDefinitions.MatchByExecutable(path).Should().BeNull();
    }

    [TestMethod]
    public void PotPlayer_UsesPlaylistFileOnly()
    {
        // Passing multiple paths to PotPlayer's command line opens only the
        // first one, so the playlist route is the only batch method.
        KnownPlayerDefinitions.PotPlayer.Capabilities.Should().Be(BatchPlayCapability.PlaylistFile);
    }

    [TestMethod]
    public void Foobar2000_IsAudioOnly()
    {
        var extensions = KnownPlayerDefinitions.Foobar2000.SupportedExtensions!;

        extensions.Should().Contain(".mp3").And.Contain(".flac");
        extensions.Should().NotContain(".mp4");
    }

    [TestMethod]
    public void VideoPlayers_AcceptBothVideoAndAudio()
    {
        foreach (var definition in KnownPlayerDefinitions.All.Where(d => d.Id != "Foobar2000"))
        {
            definition.SupportedExtensions.Should().Contain([".mp4", ".mkv", ".mp3"],
                $"{definition.Id} should take common video and audio files");
        }
    }
}
