using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Acquisition provenance never changes how a resource's bound files are discovered
/// or opened. These use the real registered providers, profile matching and cache.
/// </summary>
[TestClass]
public sealed class DownloadedResourcePlaybackTests
{
    private IServiceProvider _services = null!;
    private string _root = null!;
    private RecordingLauncher _launcher = null!;
    private IResourceService Resources => _services.GetRequiredService<IResourceService>();

    private sealed class RecordingLauncher : IBatchPlayProcessLauncher
    {
        public readonly List<(string Executable, string Arguments)> Started = [];
        public Task LaunchAsync(string executablePath, string arguments, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Started.Add((executablePath, arguments));
            return Task.CompletedTask;
        }
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-downloaded-playback", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _launcher = new RecordingLauncher();
        _services = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton<IBatchPlayProcessLauncher>(_launcher));
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        Directory.Delete(_root, true);
    }

    private async Task<Resource> Resource(ResourceSource source, bool withFiles = true)
    {
        var row = (await Resources.AddAll([new ResourceDbModel {Path = withFiles ? _root : null}])).Single();
        await _services.GetRequiredService<IResourceSourceLinkService>().Add(new ResourceSourceLink
        {
            ResourceId = row.Id,
            Source = source,
            SourceKey = source == ResourceSource.DLsite ? "RJ01234567" : "12345/abcdef"
        });
        return (await Resources.Get(row.Id))!;
    }

    private Task<ResourceProfile> AddProfile(string extension) =>
        _services.GetRequiredService<IResourceProfileService>().Add("Downloaded files", "{}", null, null,
            new ResourceProfilePlayableFileOptions {Extensions = [extension]},
            new ResourceProfilePlayerOptions
            {
                Players = [new MediaLibraryPlayer
                {
                    ExecutablePath = "configured-local-opener.exe", Extensions = [extension], Command = "--open {0}"
                }]
            }, null, 100);

    [TestMethod]
    [DataRow(ResourceSource.DLsite, "exe")]
    [DataRow(ResourceSource.DLsite, "jpg")]
    [DataRow(ResourceSource.DLsite, "mp3")]
    [DataRow(ResourceSource.DLsite, "mp4")]
    [DataRow(ResourceSource.ExHentai, "jpg")]
    [DataRow(ResourceSource.ExHentai, "mp4")]
    public async Task DownloadedFilesUseOrdinaryProfileDiscoveryCacheAndNativeOpening(ResourceSource source,
        string extension)
    {
        var file = Path.Combine(_root, $"download.{extension}");
        await File.WriteAllTextAsync(file, "downloaded file");
        await File.WriteAllTextAsync(Path.Combine(_root, "readme.txt"), "not matched by the profile");
        var resource = await Resource(source);
        await AddProfile(extension);

        var items = await Resources.DiscoverPlayableItems(resource.Id, CancellationToken.None);
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(DataOrigin.FileSystem, items[0].Origin);
        Assert.AreEqual(file, items[0].Key);

        // The resource-card cache/readiness response has just the ordinary file origin
        // too: no source page candidate can jump ahead of the matched file on App.
        var loaded = (await Resources.Get(resource.Id, ResourceAdditionalItem.PlayableItem))!;
        Assert.AreEqual(file, loaded.PlayableItems!.Single().Key);
        Assert.AreEqual(DataOrigin.FileSystem, loaded.PlayableItems.Single().Origin);
        var state = loaded.DataStates!.Single(s => s.DataType == ResourceDataType.PlayableItem);
        Assert.AreEqual(DataOrigin.FileSystem, state.Origin);
        Assert.AreEqual(DataStatus.Ready, state.Status);

        var response = await Resources.PlayItem(resource.Id, items[0].Origin, items[0].Key);
        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(("configured-local-opener.exe", $"--open \"{file}\""), _launcher.Started.Single());
        var history = await _services.GetRequiredService<IPlayHistoryService>().GetAll();
        Assert.AreEqual($"FileSystem:{file}", history.Single().Item);

        // Batch uses the same resolved files and player profile, irrespective of origin.
        var batch = await new ResourceBatchPlaySource(Resources,
            _services.GetRequiredService<IResourceProfileService>()).GetSnapshotAsync([resource.Id], CancellationToken.None);
        Assert.AreEqual(file, batch.Resources.Single().Files.Single());
        Assert.AreEqual("configured-local-opener.exe", batch.ConfiguredPlayers.Single().ExecutablePath);
    }

    [TestMethod]
    [DataRow(ResourceSource.DLsite)]
    [DataRow(ResourceSource.ExHentai)]
    public async Task AnUndownloadedResourceHasNoPlayableWebsiteOrFileState(ResourceSource source)
    {
        var resource = await Resource(source, withFiles: false);
        await AddProfile("mp4");

        Assert.AreEqual(0, (await Resources.DiscoverPlayableItems(resource.Id, CancellationToken.None)).Count);
        var loaded = (await Resources.Get(resource.Id, ResourceAdditionalItem.PlayableItem))!;
        Assert.IsTrue(loaded.PlayableItems is not {Count: > 0});
        Assert.IsFalse(loaded.DataStates?.Any(s => s.DataType == ResourceDataType.PlayableItem) == true);
        Assert.IsNull(await Resources.PickRandomPlayableItem());
    }

    [TestMethod]
    [DataRow(ResourceSource.DLsite)]
    [DataRow(ResourceSource.ExHentai)]
    public async Task MissingAndUnmatchedDownloadsNeverFallBackToTheOriginalWebsite(ResourceSource source)
    {
        var resource = await Resource(source);
        await AddProfile("mp4");
        await File.WriteAllTextAsync(Path.Combine(_root, "cover.jpg"), "image");
        _services.GetRequiredService<IBOptions<UIOptions>>().Value.Resource.DisablePlayableFileCache = true;

        Assert.AreEqual(0, (await Resources.DiscoverPlayableItems(resource.Id, CancellationToken.None)).Count);
        Assert.IsNull(await Resources.PickRandomPlayableItem());

        Directory.Delete(_root, true);
        await Assert.ThrowsAsync<IOException>(() => Resources.DiscoverPlayableItems(resource.Id, CancellationToken.None));
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "video.mp4");
        await File.WriteAllTextAsync(file, "video");
        var pick = await Resources.PickRandomPlayableItem();
        Assert.IsNotNull(pick);
        Assert.AreEqual(DataOrigin.FileSystem, pick.Origin);
        Assert.AreEqual(file, pick.Key);
    }

    [TestMethod]
    [DataRow(DataOrigin.DLsite, ResourceSource.DLsite, "RJ01234567")]
    [DataRow(DataOrigin.ExHentai, ResourceSource.ExHentai, "12345/abcdef")]
    [DataRow((DataOrigin)999, ResourceSource.DLsite, "unknown")]
    public async Task RemovedOrUnknownOriginsFailWithoutRecordingSuccessfulPlayback(DataOrigin origin,
        ResourceSource source, string key)
    {
        var resource = await Resource(source);

        var response = await Resources.PlayItem(resource.Id, origin, key);

        Assert.AreNotEqual(0, response.Code);
        Assert.AreEqual(0, _launcher.Started.Count);
        Assert.AreEqual(0, (await _services.GetRequiredService<IPlayHistoryService>().GetAll()).Count);
        Assert.IsNull((await Resources.Get(resource.Id))!.PlayedAt);
    }

    [TestMethod]
    public void RegistrationKeepsFileAndSteamProvidersButNoSourceWebpagePlayers()
    {
        var origins = _services.GetServices<IPlayableItemProvider>().Select(p => p.Origin).ToArray();
        CollectionAssert.Contains(origins, DataOrigin.FileSystem);
        CollectionAssert.Contains(origins, DataOrigin.Steam);
        CollectionAssert.DoesNotContain(origins, DataOrigin.DLsite);
        CollectionAssert.DoesNotContain(origins, DataOrigin.ExHentai);
    }
}
