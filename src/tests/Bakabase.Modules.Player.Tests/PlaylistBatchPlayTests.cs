using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Player.Services;
using Bakabase.Modules.Player.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public class PlaylistBatchPlayTests
{
    private const string VlcKey = "known|Vlc";
    private const string FoobarKey = "known|Foobar2000";
    private const int PlaylistId = 7;

    private FakeResourceService _resourceService = null!;
    private FakeResourceProfileService _profileService = null!;
    private FakePlayerExecutableLocator _locator = null!;
    private FakeBatchPlayPlaylistSource _playlistSource = null!;
    private RecordingBatchPlayLauncher _launcher = null!;
    private PlayerModuleOptions _options = null!;
    private string _mediaDir = null!;
    private string _playlistDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _resourceService = new FakeResourceService();
        _profileService = new FakeResourceProfileService();
        _locator = new FakePlayerExecutableLocator();
        _playlistSource = new FakeBatchPlayPlaylistSource();
        _launcher = new RecordingBatchPlayLauncher();
        _mediaDir = Path.Combine(Path.GetTempPath(), "bakabase-player-tests", Guid.NewGuid().ToString("N"));
        _playlistDir = Path.Combine(_mediaDir, "playlists");
        Directory.CreateDirectory(_mediaDir);
        _options = new PlayerModuleOptions { TempPlaylistDirectory = _playlistDir };
        _locator.Set("Vlc", @"C:\Program Files\VideoLAN\VLC\vlc.exe");
        _locator.Set("Foobar2000", @"C:\Program Files\foobar2000\foobar2000.exe");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_mediaDir, true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    private BatchPlayService CreateService() => new(
        _resourceService,
        _profileService,
        new PlayerDiscoveryService(_locator, NullLogger<PlayerDiscoveryService>.Instance),
        _playlistSource,
        _launcher,
        Options.Create(_options),
        NullLogger<BatchPlayService>.Instance);

    private string CreateMediaFile(string name)
    {
        var path = Path.Combine(_mediaDir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [TestMethod]
    public async Task Candidates_CountMatchedFilesPerPlayer()
    {
        _playlistSource.Set(PlaylistId, "mixed",
            new BatchPlayPlaylistEntry(CreateMediaFile("v1.mp4"), null),
            new BatchPlayPlaylistEntry(CreateMediaFile("a1.mp3"), null),
            new BatchPlayPlaylistEntry(CreateMediaFile("v2.mp4"), null));

        var candidates = await CreateService().GetPlaylistCandidatesAsync(PlaylistId, CancellationToken.None);

        candidates.Single(c => c.Key == VlcKey).MatchedFileCount.Should().Be(3);
        candidates.Single(c => c.Key == FoobarKey).MatchedFileCount.Should().Be(1);
    }

    [TestMethod]
    public async Task Candidates_IncludeProfilePlayersOfBackingResources()
    {
        _resourceService.SetResource(new Resource { Id = 1, Path = _mediaDir });
        _profileService.SetPlayerOptions(1, new ResourceProfilePlayerOptions
        {
            Players = [new MediaLibraryPlayer { ExecutablePath = @"D:\tools\CustomPlayer.exe" }],
        });
        _playlistSource.Set(PlaylistId, "pl",
            new BatchPlayPlaylistEntry(CreateMediaFile("v1.mp4"), 1));

        var candidates = await CreateService().GetPlaylistCandidatesAsync(PlaylistId, CancellationToken.None);

        candidates[0].Type.Should().Be(BatchPlayCandidateType.ProfilePlayer);
        candidates[0].DisplayName.Should().Be("CustomPlayer");
        candidates[0].MatchedFileCount.Should().Be(1, "a player without an extension set takes any file");
    }

    [TestMethod]
    public async Task Play_FiltersToPlayerSupportedFiles_AndKeepsPlaylistOrder()
    {
        _playlistSource.Set(PlaylistId, "mixed",
            new BatchPlayPlaylistEntry(CreateMediaFile("v1.mp4"), null),
            new BatchPlayPlaylistEntry(CreateMediaFile("a1.mp3"), null),
            new BatchPlayPlaylistEntry(CreateMediaFile("v2.mp4"), null));

        var result = await CreateService().PlayPlaylistAsync(PlaylistId, VlcKey, CancellationToken.None);

        result.LaunchMethod.Should().Be(BatchPlayLaunchMethod.PlaylistFile);
        result.FileCount.Should().Be(3);

        var playlistPath = Directory.EnumerateFiles(_playlistDir, "*.m3u8").Single();
        var fileLines = (await File.ReadAllLinesAsync(playlistPath))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(Path.GetFileName)
            .ToList();
        fileLines.Should().Equal("v1.mp4", "a1.mp3", "v2.mp4");
    }

    [TestMethod]
    public async Task Play_AudioOnlyPlayer_GetsOnlyTheAudioFiles()
    {
        _playlistSource.Set(PlaylistId, "mixed",
            new BatchPlayPlaylistEntry(CreateMediaFile("v1.mp4"), null),
            new BatchPlayPlaylistEntry(CreateMediaFile("a1.mp3"), null));

        var result = await CreateService().PlayPlaylistAsync(PlaylistId, FoobarKey, CancellationToken.None);

        result.FileCount.Should().Be(1);
        _launcher.Launches.Single().Arguments.Should().Contain("a1.mp3").And.NotContain("v1.mp4");
    }

    [TestMethod]
    public async Task Play_MarksDistinctBackingResourcesPlayed()
    {
        _resourceService.SetResource(new Resource { Id = 1, Path = _mediaDir });
        _resourceService.SetResource(new Resource { Id = 2, Path = _mediaDir });
        _playlistSource.Set(PlaylistId, "pl",
            new BatchPlayPlaylistEntry(CreateMediaFile("e1.mp4"), 1),
            new BatchPlayPlaylistEntry(CreateMediaFile("e2.mp4"), 1),
            new BatchPlayPlaylistEntry(CreateMediaFile("f1.mp4"), 2),
            new BatchPlayPlaylistEntry(CreateMediaFile("bare.mp4"), null));

        var result = await CreateService().PlayPlaylistAsync(PlaylistId, VlcKey, CancellationToken.None);

        result.FileCount.Should().Be(4);
        result.ResourceCount.Should().Be(2);
        var call = _resourceService.MarkPlayedCalls.Should().ContainSingle().Subject;
        call.Keys.Should().BeEquivalentTo([1, 2]);
        call[1].Should().EndWith("e1.mp4");
    }

    [TestMethod]
    public async Task Play_MissingFilesAreDroppedAndCounted()
    {
        _playlistSource.Set(PlaylistId, "pl",
            new BatchPlayPlaylistEntry(CreateMediaFile("a.mp4"), null),
            new BatchPlayPlaylistEntry(Path.Combine(_mediaDir, "deleted.mp4"), null));

        var result = await CreateService().PlayPlaylistAsync(PlaylistId, VlcKey, CancellationToken.None);

        result.FileCount.Should().Be(1);
        result.MissingFileCount.Should().Be(1);
    }

    [TestMethod]
    public async Task Play_NonexistentPlaylist_Throws()
    {
        var act = () => CreateService().PlayPlaylistAsync(999, VlcKey, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not exist*");
    }

    [TestMethod]
    public async Task Play_NoFilesThePlayerSupports_Throws()
    {
        _playlistSource.Set(PlaylistId, "videos",
            new BatchPlayPlaylistEntry(CreateMediaFile("v1.mp4"), null));

        var act = () => CreateService().PlayPlaylistAsync(PlaylistId, FoobarKey, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no existing file*");
    }

    [TestMethod]
    public async Task Play_ExceedingTheCap_Throws()
    {
        _options.MaxTotalFiles = 1;
        _playlistSource.Set(PlaylistId, "pl",
            new BatchPlayPlaylistEntry(CreateMediaFile("a.mp4"), null),
            new BatchPlayPlaylistEntry(CreateMediaFile("b.mp4"), null));

        var act = () => CreateService().PlayPlaylistAsync(PlaylistId, VlcKey, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds the limit*");
        _launcher.Launches.Should().BeEmpty();
    }
}
