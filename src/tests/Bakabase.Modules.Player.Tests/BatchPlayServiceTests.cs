using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Player.Abstractions.Models.Input;
using Bakabase.Modules.Player.Services;
using Bakabase.Modules.Player.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public class BatchPlayServiceTests
{
    private const string VlcKey = "known|Vlc";

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

    private void SeedResourceWithFiles(int id, params string[] fileNames)
    {
        _resourceService.SetResource(new Resource { Id = id, Path = _mediaDir },
            fileNames.Select(CreateMediaFile).ToArray());
    }

    private void SeedVlc() => _locator.Set("Vlc", @"C:\Program Files\VideoLAN\VLC\vlc.exe");

    private void SeedFoobar() => _locator.Set("Foobar2000", @"C:\Program Files\foobar2000\foobar2000.exe");

    // ========================================================================
    // Candidates
    // ========================================================================

    [TestMethod]
    public async Task Candidates_ProfilePlayersFirst_ThenDiscoveredKnownPlayers()
    {
        SeedVlc();
        SeedResourceWithFiles(1, "a.mp4");
        _profileService.SetPlayerOptions(1, new ResourceProfilePlayerOptions
        {
            Players = [new MediaLibraryPlayer { ExecutablePath = @"D:\tools\CustomPlayer.exe" }],
        });

        var candidates = await CreateService().GetCandidatesAsync([1], CancellationToken.None);

        candidates.Should().HaveCount(2);
        candidates[0].Type.Should().Be(BatchPlayCandidateType.ProfilePlayer);
        candidates[0].DisplayName.Should().Be("CustomPlayer");
        // Unknown players are assumed to take a playlist file.
        candidates[0].Capabilities.Should().Be(BatchPlayCapability.PlaylistFile);
        candidates[0].CapabilitiesAssumed.Should().BeTrue();
        candidates[1].Type.Should().Be(BatchPlayCandidateType.KnownPlayer);
        candidates[1].Key.Should().Be(VlcKey);
        candidates[1].CapabilitiesAssumed.Should().BeFalse();
    }

    [TestMethod]
    public async Task Candidates_ProfilePlayerRecognizedAsKnown_GetsCatalogCapabilities()
    {
        SeedResourceWithFiles(1, "a.mp4");
        _profileService.SetPlayerOptions(1, new ResourceProfilePlayerOptions
        {
            Players = [new MediaLibraryPlayer { ExecutablePath = @"D:\portable\PotPlayerMini64.exe" }],
        });

        var candidates = await CreateService().GetCandidatesAsync([1], CancellationToken.None);

        candidates.Should().ContainSingle();
        candidates[0].DisplayName.Should().Be("PotPlayer");
        candidates[0].Capabilities.Should().Be(BatchPlayCapability.PlaylistFile);
        candidates[0].CapabilitiesAssumed.Should().BeFalse();
    }

    [TestMethod]
    public async Task Candidates_SameExecutableInProfileAndDiscovery_IsDeduplicated()
    {
        const string vlcPath = @"C:\Program Files\VideoLAN\VLC\vlc.exe";
        _locator.Set("Vlc", vlcPath);
        SeedResourceWithFiles(1, "a.mp4");
        _profileService.SetPlayerOptions(1, new ResourceProfilePlayerOptions
        {
            Players = [new MediaLibraryPlayer { ExecutablePath = vlcPath }],
        });

        var candidates = await CreateService().GetCandidatesAsync([1], CancellationToken.None);

        candidates.Should().ContainSingle();
        candidates[0].Type.Should().Be(BatchPlayCandidateType.ProfilePlayer);
    }

    [TestMethod]
    public async Task Candidates_AnnotateHowManyResourcesEachPlayerCanOpen()
    {
        SeedVlc();
        SeedFoobar();
        SeedResourceWithFiles(1, "video.mp4");
        SeedResourceWithFiles(2, "song.mp3");
        SeedResourceWithFiles(3); // nothing playable

        var candidates = await CreateService().GetCandidatesAsync([1, 2, 3], CancellationToken.None);

        candidates.Single(c => c.Key == VlcKey).MatchedResourceCount.Should().Be(2);
        // Audio-only player cannot open the video resource.
        candidates.Single(c => c.Key == "known|Foobar2000").MatchedResourceCount.Should().Be(1);
    }

    [TestMethod]
    public async Task Candidates_ProfilePlayerExtensions_RestrictMatching()
    {
        SeedResourceWithFiles(1, "video.mp4");
        SeedResourceWithFiles(2, "song.mp3");
        _profileService.SetPlayerOptions(1, new ResourceProfilePlayerOptions
        {
            // User input may omit the leading dot; must still match.
            Players = [new MediaLibraryPlayer { ExecutablePath = @"D:\tools\ComicViewer.exe", Extensions = ["png"] }],
        });

        var candidates = await CreateService().GetCandidatesAsync([1, 2], CancellationToken.None);

        candidates.Single(c => c.Type == BatchPlayCandidateType.ProfilePlayer)
            .MatchedResourceCount.Should().Be(0, "no selected resource has a .png playable file");
    }

    // ========================================================================
    // PlayAsync — happy paths
    // ========================================================================

    [TestMethod]
    public async Task Play_MultipleResources_WritesPlaylistInSelectionOrder_AndLaunchesOnce()
    {
        SeedVlc();
        SeedResourceWithFiles(1, "a.mp4");
        SeedResourceWithFiles(2, "b.mp4");
        SeedResourceWithFiles(3, "c.mp4");

        var result = await CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [3, 1, 2],
            PlayerKey = VlcKey,
        }, CancellationToken.None);

        result.LaunchMethod.Should().Be(BatchPlayLaunchMethod.PlaylistFile);
        result.FileCount.Should().Be(3);
        result.ResourceCount.Should().Be(3);
        result.SkippedResources.Should().BeEmpty();

        _launcher.Launches.Should().ContainSingle();
        _launcher.Launches[0].ExecutablePath.Should().Be(@"C:\Program Files\VideoLAN\VLC\vlc.exe");

        var playlistPath = Directory.EnumerateFiles(_playlistDir, "*.m3u8").Single();
        _launcher.Launches[0].Arguments.Should().Contain(playlistPath);

        var fileLines = (await File.ReadAllLinesAsync(playlistPath))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(Path.GetFileName)
            .ToList();
        fileLines.Should().Equal("c.mp4", "a.mp4", "b.mp4");
    }

    [TestMethod]
    public async Task Play_FirstFilePerResource_TakesOnlyTheFirstFile()
    {
        SeedVlc();
        SeedResourceWithFiles(1, "e1.mp4", "e2.mp4", "e3.mp4");
        SeedResourceWithFiles(2, "f1.mp4");

        var result = await CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1, 2],
            PlayerKey = VlcKey,
            FileSelectionMode = BatchPlayFileSelectionMode.FirstFilePerResource,
        }, CancellationToken.None);

        result.FileCount.Should().Be(2);
    }

    [TestMethod]
    public async Task Play_AllFilesMode_IncludesEveryFile()
    {
        SeedVlc();
        SeedResourceWithFiles(1, "e1.mp3", "e2.mp3", "e3.mp3");
        SeedResourceWithFiles(2, "f1.mp3");

        var result = await CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1, 2],
            PlayerKey = VlcKey,
            FileSelectionMode = BatchPlayFileSelectionMode.AllFiles,
        }, CancellationToken.None);

        result.FileCount.Should().Be(4);
    }

    [TestMethod]
    public async Task Play_SingleFileSelection_SkipsThePlaylist()
    {
        SeedVlc();
        SeedResourceWithFiles(1, "only.mp4");

        var result = await CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1],
            PlayerKey = VlcKey,
        }, CancellationToken.None);

        result.LaunchMethod.Should().Be(BatchPlayLaunchMethod.MultiFileArguments);
        _launcher.Launches.Single().Arguments.Should().NotContain(".m3u8").And.Contain("only.mp4");
        Directory.Exists(_playlistDir).Should().BeFalse("no playlist should be written for a single file");
    }

    [TestMethod]
    public async Task Play_ProfilePlayerCommandTemplate_IsAppliedToThePlaylistPath()
    {
        SeedResourceWithFiles(1, "a.mp4");
        SeedResourceWithFiles(2, "b.mp4");
        const string exe = @"D:\tools\CustomPlayer.exe";
        _profileService.SetPlayerOptions(1, new ResourceProfilePlayerOptions
        {
            Players = [new MediaLibraryPlayer { ExecutablePath = exe, Command = "/playlist {0}" }],
        });

        await CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1, 2],
            PlayerKey = $"profile|{exe.ToLowerInvariant()}",
        }, CancellationToken.None);

        var (executable, arguments) = _launcher.Launches.Single();
        executable.Should().Be(exe);
        arguments.Should().StartWith("/playlist \"").And.Contain(".m3u8");
    }

    [TestMethod]
    public async Task Play_RecordsPlayHistoryForIncludedResources()
    {
        SeedVlc();
        SeedResourceWithFiles(1, "a.mp4");
        SeedResourceWithFiles(2, "b.mp4");

        await CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1, 2],
            PlayerKey = VlcKey,
        }, CancellationToken.None);

        var call = _resourceService.MarkPlayedCalls.Should().ContainSingle().Subject;
        call.Keys.Should().BeEquivalentTo([1, 2]);
        call[1].Should().EndWith("a.mp4");
    }

    [TestMethod]
    public async Task Play_AudioOnlyPlayer_SkipsVideoResources_AndPicksFirstMatchingFile()
    {
        SeedFoobar();
        SeedResourceWithFiles(1, "video.mp4"); // nothing foobar2000 can open
        SeedResourceWithFiles(2, "intro.mp4", "song.mp3"); // first *matching* file is the mp3

        var result = await CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1, 2],
            PlayerKey = "known|Foobar2000",
        }, CancellationToken.None);

        result.FileCount.Should().Be(1);
        result.SkippedResources.Should().ContainSingle()
            .Which.Should().Be(new BatchPlaySkippedResource(1, BatchPlaySkipReason.NoFilesMatchingPlayer));
        _launcher.Launches.Single().Arguments.Should().Contain("song.mp3");
    }

    // ========================================================================
    // PlayAsync — skips and guards
    // ========================================================================

    [TestMethod]
    public async Task Play_ResourcesWithoutPlayableFiles_AreSkippedButReported()
    {
        SeedVlc();
        SeedResourceWithFiles(1, "a.mp4");
        SeedResourceWithFiles(2); // no playable files
        SeedResourceWithFiles(3, "c.mp4");

        var result = await CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1, 2, 3],
            PlayerKey = VlcKey,
        }, CancellationToken.None);

        result.FileCount.Should().Be(2);
        result.SkippedResources.Should().ContainSingle()
            .Which.Should().Be(new BatchPlaySkippedResource(2, BatchPlaySkipReason.NoPlayableFiles));
    }

    [TestMethod]
    public async Task Play_MissingFilesAreDropped_AndFullyMissingResourcesSkipped()
    {
        SeedVlc();
        SeedResourceWithFiles(1, "a.mp4");
        _resourceService.SetResource(new Resource { Id = 2, Path = _mediaDir },
            Path.Combine(_mediaDir, "deleted.mp4"));

        var result = await CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1, 2],
            PlayerKey = VlcKey,
        }, CancellationToken.None);

        result.FileCount.Should().Be(1);
        result.MissingFileCount.Should().Be(1);
        result.SkippedResources.Should().ContainSingle()
            .Which.Should().Be(new BatchPlaySkippedResource(2, BatchPlaySkipReason.AllFilesMissing));
    }

    [TestMethod]
    public async Task Play_UnknownResourceIds_AreSkippedButReported()
    {
        SeedVlc();
        SeedResourceWithFiles(1, "a.mp4");

        var result = await CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1, 999],
            PlayerKey = VlcKey,
        }, CancellationToken.None);

        result.SkippedResources.Should().ContainSingle()
            .Which.Should().Be(new BatchPlaySkippedResource(999, BatchPlaySkipReason.ResourceNotFound));
    }

    [TestMethod]
    public async Task Play_AllFilesExceedingTheCap_Throws()
    {
        SeedVlc();
        _options.MaxTotalFiles = 3;
        SeedResourceWithFiles(1, "a1.mp4", "a2.mp4");
        SeedResourceWithFiles(2, "b1.mp4", "b2.mp4");

        var act = () => CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1, 2],
            PlayerKey = VlcKey,
            FileSelectionMode = BatchPlayFileSelectionMode.AllFiles,
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds the limit*");
        _launcher.Launches.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Play_NoPlayableFilesAnywhere_Throws()
    {
        SeedVlc();
        SeedResourceWithFiles(1);

        var act = () => CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1],
            PlayerKey = VlcKey,
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Play_UnknownPlayerKey_Throws()
    {
        SeedResourceWithFiles(1, "a.mp4");

        var act = () => CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1],
            PlayerKey = "known|NotInstalled",
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no longer available*");
    }

    [TestMethod]
    public async Task Play_EmptySelection_Throws()
    {
        var act = () => CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [],
            PlayerKey = VlcKey,
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Play_LauncherFailure_SurfacesAsUserFacingError()
    {
        SeedVlc();
        SeedResourceWithFiles(1, "a.mp4");
        SeedResourceWithFiles(2, "b.mp4");
        _launcher.ThrowOnLaunch = new Exception("The system cannot find the file specified.");

        var act = () => CreateService().PlayAsync(new BatchPlayInputModel
        {
            ResourceIds = [1, 2],
            PlayerKey = VlcKey,
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Failed to launch VLC media player:*");
    }
}
