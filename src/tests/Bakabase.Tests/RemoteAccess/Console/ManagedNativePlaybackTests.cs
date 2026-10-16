using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

[TestClass]
public class ManagedNativePlaybackTests
{
    [DataTestMethod]
    [DataRow("play")]
    [DataRow("directory")]
    [DataRow("batch-play")]
    [DataRow("batch-play/candidates")]
    public async Task AManagedPageCannotLaunchAPlayerThroughItsOwnManagedDevice(string action)
    {
        await using var console = await ConsoleHarness.StartAsync();
        await using var owner = await FakeServer.StartAsync("owner", "Owner", 46900);
        await console.AddManagedAsync(owner);
        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync(owner.ServerId, null))!.Url);
        var path = "/federation/local/management/third-device/" + action;
        using var response = await ConsoleHarness.SendToRelayAsync(port, path, System.Net.Http.HttpMethod.Post, "{}");
        Assert.AreEqual(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        StringAssert.Contains(body.RootElement.GetProperty("message").GetString()!, "this app's own library");
        Assert.IsFalse(response.Headers.Contains("X-Bakabase-Client"),
            "This is an intentional route refusal, not a missing-handler upgrade prompt.");
        Assert.IsFalse(owner.Requests.Any(r => r.Path == path));
    }

    [TestMethod]
    public async Task NativeHistoryRemainsBoundToTheSelectedSourceLibrary()
    {
        await using var console = await ConsoleHarness.StartAsync();
        await using var owner = await FakeServer.StartAsync("owner", "Owner", 46900);
        await console.AddManagedAsync(owner);
        await console.Manager.OpenAsync(owner.ServerId, null);
        var upstream = console.Manager.RelayServices(owner.ServerId)!.GetRequiredService<IUpstreamApi>();
        await upstream.MarkPlayedForEpochAsync(42, "FileSystem:/movie.mp4", "source-epoch");
        var recorded = owner.Requests.Single(r => r.Path == "/resource/42/played-at");
        StringAssert.Contains(recorded.Query, "managedLibraryEpoch=source-epoch");
        Assert.AreEqual(true, recorded.SignatureValid);
    }

    [TestMethod]
    public async Task UnifiedDirectoryLookupIsBoundToTheSelectedSourceEpoch()
    {
        await using var console = await ConsoleHarness.StartAsync();
        await using var owner = await FakeServer.StartAsync("owner", "Owner", 46900);
        await console.AddManagedAsync(owner);
        await console.Manager.OpenAsync(owner.ServerId, null);
        var upstream = console.Manager.RelayServices(owner.ServerId)!.GetRequiredService<IUpstreamApi>();
        await upstream.GetResourceForEpochAsync(42, "source-epoch");
        var request = owner.Requests.Single(r => r.Path == "/resource/keys");
        StringAssert.Contains(request.Query, "managedLibraryEpoch=source-epoch");
        Assert.AreEqual(true, request.SignatureValid);
    }

    [TestMethod]
    public async Task UnifiedBatchSnapshotRemainsBoundToTheSelectedSourceEpoch()
    {
        await using var console = await ConsoleHarness.StartAsync();
        await using var owner = await FakeServer.StartAsync("owner", "Owner", 46900);
        await console.AddManagedAsync(owner);
        await console.Manager.OpenAsync(owner.ServerId, null);
        var upstream = console.Manager.RelayServices(owner.ServerId)!.GetRequiredService<IUpstreamApi>();
        await upstream.GetBatchPlayResourceSnapshotForEpochAsync([42, 43], "source-epoch");
        var request = owner.Requests.Single(r => r.Path == "/player/batch-play/resource-snapshot");
        StringAssert.Contains(request.Query, "managedLibraryEpoch=source-epoch");
        Assert.AreEqual(true, request.SignatureValid);
    }

    [TestMethod]
    public async Task UnifiedBatchCandidateBridgeUsesTheExistingScopedBatchServiceWithoutLaunching()
    {
        await using var console = await ConsoleHarness.StartAsync();
        await using var owner = await FakeServer.StartAsync("owner", "Owner", 46900);
        owner.HandleRequest = async context =>
        {
            if (context.Request.Path != "/player/batch-play/resource-snapshot") return false;
            await context.Response.WriteAsJsonAsync(new {code = 0, data = new {resources = Array.Empty<object>(), configuredPlayers = Array.Empty<object>(), playerOptionsByResourceId = new Dictionary<string, object>()}});
            return true;
        };
        await console.AddManagedAsync(owner);
        var response = await console.Manager.BatchPlayManagedResourcesAsync(owner.ServerId, [42], "source-epoch", null, 1);
        Assert.AreEqual(200, response.StatusCode, response.Body);
        var request = owner.Requests.Single(r => r.Path == "/player/batch-play/resource-snapshot");
        StringAssert.Contains(request.Query, "managedLibraryEpoch=source-epoch");
        Assert.AreEqual(true, request.SignatureValid);
    }

    [TestMethod]
    public async Task UnifiedNativeOpeningUsesTheExistingMappedFileAndSourceProfilePipeline()
    {
        var profiles = new RefusingProfiles();
        await using var console = await ConsoleHarness.StartAsync(services: services =>
            services.AddSingleton<ISourceResourceProfilePlaybackService>(profiles));
        await using var owner = await FakeServer.StartAsync("owner", "Owner", 46900);
        await console.AddManagedAsync(owner);
        var localFile = Path.Combine(console.Root, "movie.mp4");
        await File.WriteAllTextAsync(localFile, "fixture");
        await console.Manager.SetPathMappingsAsync(owner.ServerId,
            [new ManagedServerPathMapping("/library", console.Root)]);

        // The profile deliberately refuses before any OS launch; no application is started by this test.
        var response = await console.Manager.PlayManagedResourceAsync(owner.ServerId, 42,
            DataOrigin.FileSystem, "/library/movie.mp4", "source-epoch", explicitChoice: true);

        Assert.AreEqual(409, response.StatusCode);
        StringAssert.Contains(response.Body, "local resource profiles");
        Assert.AreEqual(owner.ServerId, profiles.SourceId);
        Assert.AreEqual("source-epoch", profiles.Epoch);
        CollectionAssert.AreEqual(new[] {42}, profiles.ResourceIds);
        Assert.IsTrue(console.Manager.RunningRelays.ContainsKey(owner.ServerId));
    }

    private sealed class RefusingProfiles : ISourceResourceProfilePlaybackService
    {
        public string? SourceId;
        public string? Epoch;
        public int[] ResourceIds = [];

        public Task<ResourceProfileOpeningOptions> ResolveAsync(string sourceDeviceId, string? libraryEpoch,
            int resourceId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<IReadOnlyDictionary<int, ResourceProfileOpeningOptions>> ResolveManyAsync(string sourceDeviceId,
            string? libraryEpoch, IReadOnlyList<int> resourceIds, CancellationToken cancellationToken = default)
        {
            SourceId = sourceDeviceId;
            Epoch = libraryEpoch;
            ResourceIds = resourceIds.ToArray();
            throw new InvalidOperationException("Refuse before an OS player could launch.");
        }
    }
}
