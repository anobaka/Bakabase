using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Service.Components.Federation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationDirectoryTests
{
    [TestMethod]
    public async Task OpensContainingDirectoryForNonMediaFilesWithoutExposingAbsoluteSourcePaths()
    {
        await using var fixture = await Fixture.Create();
        var file = Path.Combine(fixture.Directory.Path, "a quoted ' document.txt");
        await File.WriteAllTextAsync(file, "fixture");
        fixture.Db.ResourcesV2.Add(new ResourceDbModel { Id = 1, Path = file, IsFile = true });
        await fixture.Db.SaveChangesAsync();
        var reference = new ResourceRef(fixture.Identity.NodeId, fixture.Identity.LibraryEpoch, 1);
        var metadata = (await fixture.Local.GetLocationAsync("local-library", reference, default)).Response;
        Assert.IsNotNull(metadata.Location);
        Assert.AreEqual(Path.GetFileName(file), metadata.Location.RelativePath);
        Assert.IsFalse(JsonSerializer.Serialize(metadata).Contains(fixture.Directory.Path));
        Assert.IsTrue((await fixture.Service.OpenAsync(reference, default)).Opened);
        CollectionAssert.AreEqual(new[] { fixture.Directory.Path }, fixture.Opener.Opened.ToArray());
        await Assert.ThrowsExactlyAsync<FederationQueryException>(() => fixture.Service.OpenAsync(reference with { LibraryEpoch = "old-epoch" }, default));
        Assert.AreEqual(1, fixture.Opener.Opened.Count);
    }

    [TestMethod]
    public async Task RemoteDirectoryRequiresCurrentSourceAuthorizationAndExistingBoundedMapping()
    {
        await using var fixture = await Fixture.Create();
        var reference = new ResourceRef("remote", "remote-epoch", 1);
        fixture.Transport.Response = new(reference, new("source-root", ".", true));
        Assert.AreEqual("PathMappingRequired", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            fixture.Service.OpenAsync(reference, default))).Code);
        await fixture.Peers.SetPathMappingsAsync("remote", [new("source-root", fixture.Directory.Path)]);
        await fixture.Service.OpenAsync(reference, default);
        Assert.AreEqual(MediaPathBoundary.ResolvePhysical(fixture.Directory.Path), fixture.Opener.Opened.Single());
        fixture.Transport.Response = new(reference, new("source-root", "../outside", true));
        Assert.AreEqual("MappedPathUnavailable", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            fixture.Service.OpenAsync(reference, default))).Code);
        fixture.Transport.Status = HttpStatusCode.Forbidden;
        Assert.AreEqual("GrantRevoked", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            fixture.Service.OpenAsync(reference, default))).Code);
        Assert.AreEqual(1, fixture.Opener.Opened.Count);
        Assert.IsTrue(fixture.Transport.Paths.All(p => p == "/federation/v1/export/resources/location"));
    }

    [TestMethod]
    public async Task MappingUpdatesCompareThePreviewInsideTheAtomicWrite()
    {
        await using var fixture = await Fixture.Create();
        var first = new NodePathMapping("source-root", fixture.Directory.Path);
        await fixture.Peers.SetPathMappingsAsync("remote", [first], expectedMappings: []);
        var error = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            fixture.Peers.SetPathMappingsAsync("remote", [], expectedMappings: []));
        Assert.AreEqual("PathMappingsChanged", error.ErrorCode);
        CollectionAssert.AreEqual(new[] { first }, (await fixture.Peers.GetPathMappingsAsync("remote")).ToArray());
        await fixture.Peers.SetPathMappingsAsync("remote", [], expectedMappings: [first]);
        Assert.AreEqual(0, (await fixture.Peers.GetPathMappingsAsync("remote")).Count);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RevokingOutboundAfterReadingLocationNeverLaunchesMappedDirectory(bool forget)
    {
        await using var fixture = await Fixture.Create();
        var reference = new ResourceRef("remote", "remote-epoch", 1);
        fixture.Transport.Response = new(reference, new("source-root", ".", true));
        await fixture.Peers.SetPathMappingsAsync("remote", [new("source-root", fixture.Directory.Path)]);
        fixture.Transport.AfterBodyRead = () => forget
            ? fixture.Peers.ForgetOutboundAsync("remote")
            : fixture.Peers.SetEnabledAsync("remote", false);

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.OpenAsync(reference, default));

        Assert.AreEqual(0, fixture.Opener.Opened.Count);
        var peer = (await fixture.Peers.GetStatusAsync()).Peers.Single(p => p.NodeId == "remote");
        Assert.IsFalse(peer.Enabled);
        if (forget) Assert.IsNull(peer.OutboundGrant);
        else Assert.IsNotNull(peer.OutboundGrant);
        Assert.AreEqual(1, (await fixture.Peers.GetPathMappingsAsync("remote")).Count,
            "The existing mapping alone must not let an old request survive revocation.");
    }

    [TestMethod]
    public async Task HeadlessAndForgedIdentityNeverLaunchTheFileManager()
    {
        await using var fixture = await Fixture.Create();
        fixture.Opener.Available = false;
        Assert.AreEqual("OpenDirectoryUnavailable", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            fixture.Service.OpenAsync(new("remote", "remote-epoch", 1), default))).Code);
        fixture.Opener.Available = true;
        fixture.Transport.Response = new(new("other-node", "remote-epoch", 1), new("source-root", ".", true));
        Assert.AreEqual("InvalidPeerResponse", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            fixture.Service.OpenAsync(new("remote", "remote-epoch", 1), default))).Code);
        Assert.AreEqual(0, fixture.Opener.Opened.Count);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly FederationBrowsingControlTests.StateDirectory Directory = new();
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly GrantLeaseRegistry _grants = new();
        public BakabaseDbContext Db = null!;
        public NodeIdentity Identity = null!;
        public FederationPeerService Peers = null!;
        public FederationResourceService Local = null!;
        public FederationDirectoryService Service = null!;
        public readonly RecordingOpener Opener = new();
        public readonly RecordingTransport Transport = new();
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            await f._connection.OpenAsync();
            f.Db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(f._connection).Options);
            await f.Db.Database.EnsureCreatedAsync();
            // Start from a persisted direct outbound authorization, so disable/forget exercise
            // the production grant cancellation path instead of cancelling a test-only token.
            await File.WriteAllTextAsync(Path.Combine(f.Directory.Ensure(), FederationStateStore.FileName),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1, nodeId = "self", libraryEpoch = Guid.NewGuid().ToString("N"),
                    peers = new Dictionary<string, object>
                    {
                        ["remote"] = new { nodeId = "remote", label = "Remote", address = "http://remote.invalid",
                            libraryEpoch = "remote-epoch", enabled = true, pathMappings = Array.Empty<NodePathMapping>() }
                    },
                    outboundGrants = new Dictionary<string, NodeCredentials>
                    {
                        ["remote"] = new("grant", "self", "remote", "remote-epoch", NodeRequestSignature.RandomToken(), 1)
                    }
                }, FederationJson.Options));
            var store = new FederationStateStore(f.Directory, f.Directory);
            var identity = new NodeIdentityProvider(store);
            f.Identity = await identity.GetAsync();
            f.Peers = new(store, identity, f._grants, TimeProvider.System);
            await f.Peers.SetSharingAsync(true);
            await f.Peers.RequestPairingAsync(new("remote", "Remote", "transaction", NodeRequestSignature.RandomToken()));
            await f.Peers.ApproveAsync("transaction");
            f.Local = new(null!, null!, identity, new Access(), new AssetLeaseStore(), f.Db);
            f.Service = new(identity, f.Local, new Sessions(), f.Transport, f.Peers, f.Opener, f._grants);
            return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); _grants.Dispose(); Directory.Dispose(); }
    }
    private sealed class Access : IFederationQueryAccess
    {
        public Task<QueryAccess> ValidateAsync(string grantId, string expectedLibraryEpoch, CancellationToken cancellationToken) =>
            Task.FromResult(new QueryAccess("self", expectedLibraryEpoch, grantId, 1));
    }
    private sealed class RecordingOpener : IFederationDirectoryOpener
    {
        public bool Available { get; set; } = true;
        public readonly List<string> Opened = [];
        public void Open(string directory, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Opened.Add(directory);
        }
    }
    private sealed class Sessions : IPeerSessionFactory
    {
        public Task<PeerSessionSnapshot> GetAsync(string nodeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PeerSessionSnapshot(nodeId, "http://remote.invalid", new("grant", "self", nodeId, "remote-epoch", "unused", 1),
                TimeSpan.Zero, new(nodeId, "remote-epoch", "Remote", 1, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow));
    }
    private sealed class RecordingTransport : INodeTransport
    {
        public ResourceLocationResponse Response = null!;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Func<Task>? AfterBodyRead;
        public readonly List<string> Paths = [];
        public Task<HttpResponseMessage> SendAsync(string nodeId, HttpMethod method, string relativePath, object? body = null,
            CancellationToken cancellationToken = default) => throw new AssertFailedException("Expected a pinned peer session.");
        public Task<HttpResponseMessage> SendAsync(PeerSessionSnapshot session, HttpMethod method, string relativePath, object? body = null,
            CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? headers = null)
        {
            Paths.Add(relativePath);
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StreamContent(new CompletionStream(JsonSerializer.SerializeToUtf8Bytes(Response, FederationJson.Options),
                    AfterBodyRead))
            });
        }
    }

    private sealed class CompletionStream(byte[] bytes, Func<Task>? completed) : MemoryStream(bytes)
    {
        private bool _completed;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = base.Read(buffer.Span);
            if (count == 0 && !_completed)
            {
                _completed = true;
                if (completed != null) await completed();
            }
            return count;
        }
    }
}
