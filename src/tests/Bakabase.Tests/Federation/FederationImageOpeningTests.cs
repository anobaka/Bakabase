using System.Net;
using System.Reflection;
using System.Text.Json;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Federation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationImageOpeningTests
{
    [TestMethod]
    public async Task MappedImageUsesOnlyTheViewingComputersDefaultAssociationAfterSourceAuthorization()
    {
        await using var f = await Fixture.Create();
        const string name = "a quoted ' image.JPG";
        var file = await f.WriteFile(name);
        await f.MapRoot();
        var reference = f.RememberImage(name, name);
        var result = await f.Media.PrepareAsync(new(reference, "player"), "http://127.0.0.1:34567", default);

        Assert.IsTrue(result.Launched);
        Assert.IsNull(result.Url);
        CollectionAssert.AreEqual(new[] {MediaPathBoundary.ResolvePhysical(file)}, f.Images.Opened.ToArray());
        CollectionAssert.AreEqual(new[] {HttpMethod.Head}, f.Transport.Methods.ToArray());
        Assert.AreEqual(0, f.Launcher.Launches.Count, "An image must never be passed to an AV executable.");
        Assert.AreEqual(0, f.Resources.Played.Count, "Read-only sharing never edits the source's history.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnmappedOrMissingImageOffersPreviewWithoutHandingAnHttpUrlToTheShell(bool missing)
    {
        await using var f = await Fixture.Create();
        if (missing) await f.MapRoot();
        else await f.WriteFile("cover.jpg");
        var reference = f.RememberImage("cover.jpg", "cover.jpg");
        var error = await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Media.PrepareAsync(new(reference, "player"), "http://127.0.0.1:34567", default));
        Assert.AreEqual("MappedPathUnavailable", error.Code);
        StringAssert.Contains(error.Message, "preview");
        Assert.AreEqual(0, f.Images.Opened.Count);
        Assert.AreEqual(0, f.Launcher.Launches.Count);

        var preview = await f.Media.PrepareAsync(new(reference, "preview"), "http://127.0.0.1:34567", default);
        Assert.IsFalse(preview.Launched);
        StringAssert.StartsWith(preview.Url!, "http://127.0.0.1:34567/federation/local/media/");
        Assert.AreEqual(0, f.Images.Opened.Count);
    }

    [TestMethod]
    [DataRow("../outside.jpg")]
    [DataRow("/outside.jpg")]
    [DataRow("C:/outside.jpg")]
    [DataRow(@"..\outside.jpg")]
    [DataRow("nested//outside.jpg")]
    public async Task InvalidRelativePathsNeverReachTheLocalImageOpener(string relativePath)
    {
        await using var f = await Fixture.Create();
        await f.MapRoot();
        var reference = f.RememberImage("outside.jpg", relativePath);
        var error = await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Media.PrepareAsync(new(reference, "player"), "http://127.0.0.1:34567", default));
        Assert.AreEqual("MappedPathUnavailable", error.Code);
        Assert.AreEqual(0, f.Images.Opened.Count);
    }

    [TestMethod]
    [DataRow("payload.exe")]
    [DataRow("payload.lnk")]
    [DataRow("payload.js")]
    [DataRow("payload.ps1")]
    [DataRow("payload.bat")]
    [DataRow("different.png")]
    public async Task SafeDeclaredNameCannotDisguiseAnExecutableOrDifferentLocalImageType(string target)
    {
        await using var f = await Fixture.Create();
        await f.MapRoot();
        await f.WriteFile(target);
        var reference = f.RememberImage("cover.jpg", target);
        var error = await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Media.PrepareAsync(new(reference, "player"), "http://127.0.0.1:34567", default));
        Assert.AreEqual("UnsupportedPlaybackMode", error.Code);
        Assert.AreEqual(0, f.Images.Opened.Count);
        Assert.AreEqual(0, f.Launcher.Launches.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SymlinkEscapeOrExecutableTargetIsRefusedBeforeNativeOpening(bool executableInsideRoot)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating filesystem links requires an optional Windows privilege.");
        await using var f = await Fixture.Create();
        await f.MapRoot();
        var target = executableInsideRoot ? await f.WriteFile("payload.exe") :
            Path.Combine(f.Directory.Path, "outside.jpg");
        if (!executableInsideRoot) await File.WriteAllTextAsync(target, "fixture");
        // macOS temporary paths may use /var while their physical parent is /private/var.
        // Use the canonical existing target so the inside-root case reaches the image-type guard.
        File.CreateSymbolicLink(Path.Combine(f.MediaRoot, "cover.jpg"), MediaPathBoundary.ResolvePhysical(target));
        if (executableInsideRoot)
            Assert.IsNotNull(MediaPathBoundary.Map(f.MediaRoot, "cover.jpg"),
                "The executable-target link must remain within the mapped root before checking its image type.");
        else
            Assert.IsNull(MediaPathBoundary.Map(f.MediaRoot, "cover.jpg"),
                "The outside-root link must be rejected by the mapping boundary.");
        var reference = f.RememberImage("cover.jpg", "cover.jpg");
        var error = await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Media.PrepareAsync(new(reference, "player"), "http://127.0.0.1:34567", default));
        Assert.AreEqual(executableInsideRoot ? "UnsupportedPlaybackMode" : "MappedPathUnavailable", error.Code);
        Assert.AreEqual(0, f.Images.Opened.Count);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Forbidden, "GrantRevoked")]
    [DataRow(HttpStatusCode.Gone, "AssetExpired")]
    public async Task SourceRefusalWinsOverAnExistingMappedImage(HttpStatusCode status, string code)
    {
        await using var f = await Fixture.Create();
        await f.WriteFile("cover.jpg");
        await f.MapRoot();
        f.Transport.Status = status;
        var reference = f.RememberImage("cover.jpg", "cover.jpg");
        var error = await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Media.PrepareAsync(new(reference, "player"), "http://127.0.0.1:34567", default));
        Assert.AreEqual(code, error.Code);
        Assert.AreEqual(0, f.Images.Opened.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisablingOrForgettingThePeerAfterHeadStillCancelsTheNativeOpen(bool forget)
    {
        await using var f = await Fixture.Create();
        await f.WriteFile("cover.jpg");
        await f.MapRoot();
        var reference = f.RememberImage("cover.jpg", "cover.jpg");
        f.Transport.AfterHead = () => forget ? f.Peers.ForgetOutboundAsync("remote") :
            f.Peers.SetEnabledAsync("remote", false);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            f.Media.PrepareAsync(new(reference, "player"), "http://127.0.0.1:34567", default));
        Assert.AreEqual(0, f.Images.Opened.Count);
    }

    [TestMethod]
    public async Task OwnImageUsesItsActualAuthorizedLeaseAndKeepsLocalPlayedHistory()
    {
        await using var f = await Fixture.Create();
        var file = await f.WriteFile("own image.png");
        var reference = await f.RememberOwnImage(file);
        var result = await f.Media.PrepareAsync(new(reference, "player"), "http://localhost:34567", default);
        Assert.IsTrue(result.Launched);
        CollectionAssert.AreEqual(new[] {MediaPathBoundary.ResolvePhysical(file)}, f.Images.Opened.ToArray());
        Assert.AreEqual("FileSystem:" + file, f.Resources.Played.Single()[42]);
        Assert.AreEqual(0, f.Transport.Methods.Count);
        Assert.AreEqual(0, f.Launcher.Launches.Count);
    }

    [TestMethod]
    public async Task OwnResourceDetailAdvertisesAnExistingAuthorizedImageWithoutNetworkRequests()
    {
        await using var f = await Fixture.Create();
        var reference = await f.RememberOwnImage(await f.WriteFile("own image.png"));
        var response = await f.Media.ResolveAsync(new([reference.ResourceRef]), default);
        Assert.IsTrue(response.Resources.Single().Assets.Single().CanOpenLocally!.Value);
        Assert.AreEqual(0, f.Transport.Methods.Count);
        Assert.AreEqual(0, f.Images.Opened.Count);
    }

    [TestMethod]
    public async Task ImagesOutsideTheExistingImageExtensionAllowListRemainPreviewOnly()
    {
        await using var f = await Fixture.Create();
        await f.MapRoot();
        await f.WriteFile("cover.avif");
        var reference = f.RememberImage("cover.avif", "cover.avif");
        Assert.AreEqual("UnsupportedPlaybackMode", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Media.PrepareAsync(new(reference, "player"), "http://localhost:34567", default))).Code);
        Assert.AreEqual(0, f.Images.Opened.Count);
    }

    [TestMethod]
    public void HeadlessHostNeverAdvertisesNativeImageOpeningEvenOnADesktopOperatingSystem()
    {
        var opener = new FederationImageOpener(new ServerSelfDescription(() => ServerKind.Headless));
        Assert.IsFalse(opener.Available);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DetailCapabilityIsComputedHereAndNeverNeedsPerImageHeadRequests(bool exists)
    {
        await using var f = await Fixture.Create();
        await f.MapRoot();
        if (exists) await f.WriteFile("cover.jpg");
        var reference = f.RememberImage("cover.jpg", "cover.jpg");
        f.Transport.Detail = Fixture.Detail(reference.ResourceRef,
            f.Sessions.GetAsset(reference).Asset with {CanOpenLocally = !exists});
        var response = await f.Media.ResolveAsync(new([reference.ResourceRef]), default);
        Assert.AreEqual(exists, response.Resources.Single().Assets.Single().CanOpenLocally);
        CollectionAssert.AreEqual(new[] {HttpMethod.Post}, f.Transport.Methods.ToArray());
        Assert.AreEqual(0, f.Images.Opened.Count);
    }

    [TestMethod]
    public async Task UnavailableDesktopCannotAdvertiseOrOpenAMappedImage()
    {
        await using var f = await Fixture.Create();
        await f.MapRoot();
        await f.WriteFile("cover.jpg");
        var reference = f.RememberImage("cover.jpg", "cover.jpg");
        f.Images.Available = false;
        f.Transport.Detail = Fixture.Detail(reference.ResourceRef,
            f.Sessions.GetAsset(reference).Asset with {CanOpenLocally = true});
        Assert.IsFalse((await f.Media.ResolveAsync(new([reference.ResourceRef]), default))
            .Resources.Single().Assets.Single().CanOpenLocally!.Value);
        Assert.AreEqual("PlayerUnavailable", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Media.PrepareAsync(new(reference, "player"), "http://localhost:34567", default))).Code);
        Assert.AreEqual(0, f.Images.Opened.Count);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly FederationBrowsingControlTests.StateDirectory Directory = new();
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly GrantLeaseRegistry _grants = new();
        private readonly AssetLeaseStore _assets = new();
        private BakabaseDbContext _db = null!;
        private FederationResourceService _local = null!;
        private NodeIdentity _identity = null!;
        public string MediaRoot => Path.Combine(Directory.Path, "media");
        public readonly FederationMediaSessions Sessions = new();
        public readonly ImageOpener Images = new();
        public readonly RecordingLauncher Launcher = new();
        public readonly Transport Transport = new();
        public ResourceProxy Resources = null!;
        public FederationMediaService Media = null!;
        public FederationPeerService Peers = null!;

        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            System.IO.Directory.CreateDirectory(f.MediaRoot);
            await f._connection.OpenAsync();
            f._db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(f._connection).Options);
            await f._db.Database.EnsureCreatedAsync();
            await File.WriteAllTextAsync(Path.Combine(f.Directory.Ensure(), FederationStateStore.FileName),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1, nodeId = "self", libraryEpoch = Guid.NewGuid().ToString("N"),
                    peers = new Dictionary<string, object>
                    {
                        ["remote"] = new {nodeId = "remote", label = "Remote", address = "http://remote.invalid",
                            libraryEpoch = "remote-epoch", enabled = true, pathMappings = Array.Empty<NodePathMapping>()}
                    },
                    outboundGrants = new Dictionary<string, NodeCredentials>
                    {
                        ["remote"] = new("grant", "self", "remote", "remote-epoch", NodeRequestSignature.RandomToken(), 1)
                    }
                }, FederationJson.Options));
            var store = new FederationStateStore(f.Directory, f.Directory);
            var identity = new NodeIdentityProvider(store);
            f._identity = await identity.GetAsync();
            f.Peers = new(store, identity, f._grants, TimeProvider.System);
            var resources = DispatchProxy.Create<IResourceService, ResourceProxy>();
            f.Resources = (ResourceProxy) resources;
            f._local = new(resources, DispatchProxy.Create<IPropertyValueScopeResolver, EmptyScopes>(), identity,
                new Access(), f._assets, f._db);
            var directories = new FederationDirectoryService(identity, f._local, new PeerSessions(), f.Transport,
                f.Peers, new DirectoryOpener(), f._grants);
            f.Media = new(identity, f._local, new PeerSessions(), f.Transport, f.Peers, f.Sessions, null!,
                f.Launcher, resources, f._grants, directories, f.Images);
            return f;
        }

        public async Task<string> WriteFile(string name)
        {
            var path = Path.Combine(MediaRoot, name);
            await File.WriteAllTextAsync(path, "isolated fixture");
            return path;
        }

        public Task MapRoot() => Peers.SetPathMappingsAsync("remote", [new("source-root", MediaRoot)]);

        public AssetRef RememberImage(string fileName, string relativePath)
        {
            var reference = new ResourceRef("remote", "remote-epoch", 42);
            var content = MediaContentTypes.Get(fileName)!.Value.ContentType;
            var asset = new FederatedAsset(new string('a', 64), "image", fileName, content,
                "source-root", relativePath, DateTimeOffset.UtcNow.AddMinutes(5));
            Sessions.Remember(Detail(reference, asset), PeerSessions.Snapshot("remote"));
            return new(reference, asset.AssetId);
        }

        public async Task<AssetRef> RememberOwnImage(string path)
        {
            Resources.Resource = new Resource {Id = 42, Path = path, IsFile = true};
            _db.ResourcesV2.Add(new ResourceDbModel {Id = 42, Path = path, IsFile = true});
            await _db.SaveChangesAsync();
            var reference = new ResourceRef(_identity.NodeId, _identity.LibraryEpoch, 42);
            var lease = _assets.Issue(reference, FederationQueryAccess.LocalGrantId, 1, path,
                MediaContentTypes.Get(path)!.Value.ContentType, "image");
            Sessions.Remember(Detail(reference, new(lease.AssetId, "image", Path.GetFileName(path), lease.ContentType,
                null, null, lease.ExpiresAt)), null);
            return new(reference, lease.AssetId);
        }

        public static FederatedResourceDetail Detail(ResourceRef reference, FederatedAsset asset) =>
            new(reference, "Source", "Images", asset.FileName, "HasFile", [], [], [], [], [asset], null);

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
            _grants.Dispose();
            Directory.Dispose();
        }
    }

    public class ResourceProxy : DispatchProxy
    {
        public Resource? Resource;
        public readonly List<IReadOnlyDictionary<int, string>> Played = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IResourceService.Get)) return Task.FromResult(Resource);
            if (method.Name == nameof(IResourceService.MarkPlayed))
            {
                Played.Add((IReadOnlyDictionary<int, string>) args![0]!);
                return Task.CompletedTask;
            }
            throw new AssertFailedException("Unexpected resource call: " + method.Name);
        }
    }

    public class EmptyScopes : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == nameof(IPropertyValueScopeResolver.Resolve) ? null :
                throw new AssertFailedException("Unexpected property call: " + method.Name);
    }

    private sealed class Access : IFederationQueryAccess
    {
        public Task<QueryAccess> ValidateAsync(string grantId, string expectedLibraryEpoch, CancellationToken ct) =>
            Task.FromResult(new QueryAccess("self", expectedLibraryEpoch, grantId, 1));
    }

    private sealed class ImageOpener : IFederationImageOpener
    {
        public bool Available { get; set; } = true;
        public readonly List<string> Opened = [];
        public void Open(string imagePath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Assert.IsTrue(Path.IsPathFullyQualified(imagePath));
            Assert.IsTrue(File.Exists(imagePath));
            Opened.Add(imagePath);
        }
    }

    private sealed class RecordingLauncher : IBatchPlayProcessLauncher
    {
        public readonly List<string> Launches = [];
        public Task LaunchAsync(string executablePath, string arguments, CancellationToken ct)
        {
            Launches.Add(executablePath);
            return Task.CompletedTask;
        }
    }

    private sealed class DirectoryOpener : IFederationDirectoryOpener
    {
        public bool Available => true;
        public void Open(string directory, CancellationToken ct = default) =>
            throw new AssertFailedException("Image actions must not launch a file manager.");
    }

    private sealed class PeerSessions : IPeerSessionFactory
    {
        public static PeerSessionSnapshot Snapshot(string nodeId) => new(nodeId, "http://remote.invalid",
            new("grant", "self", nodeId, "remote-epoch", "unused", 1), TimeSpan.Zero,
            new(nodeId, "remote-epoch", "Remote", 1, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
        public Task<PeerSessionSnapshot> GetAsync(string nodeId, CancellationToken ct = default) =>
            Task.FromResult(Snapshot(nodeId));
    }

    private sealed class Transport : INodeTransport
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public FederatedResourceDetail? Detail;
        public Func<Task>? AfterHead;
        public readonly List<HttpMethod> Methods = [];
        public Task<HttpResponseMessage> SendAsync(string nodeId, HttpMethod method, string relativePath,
            object? body = null, CancellationToken ct = default) =>
            throw new AssertFailedException("Expected a pinned peer session.");
        public async Task<HttpResponseMessage> SendAsync(PeerSessionSnapshot session, HttpMethod method,
            string relativePath, object? body = null, CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            Methods.Add(method);
            if (method == HttpMethod.Head && AfterHead != null) await AfterHead();
            return new HttpResponseMessage(Status)
            {
                Content = new ByteArrayContent(method == HttpMethod.Post
                    ? JsonSerializer.SerializeToUtf8Bytes(new ResourceResolveResponse([Detail!]), FederationJson.Options)
                    : [])
            };
        }
    }
}
