using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Models.View;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bootstrap.Models.ResponseModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class SourceResourceProfileServiceTests
{
    [TestMethod]
    public async Task Source_list_uses_validated_metadata_name_and_epoch_without_changing_the_connection()
    {
        using var directory = new SourceDirectory();
        var state = new FederationStateStore(directory, directory);
        var peers = new FederationPeerService(state, new NodeIdentityProvider(state), new(), TimeProvider.System);
        var registry = new MetadataReplicaRegistry(directory);
        var manager = new FakeManagedServerService();
        var service = new SourceResourceProfileService(null!, peers, null!, null!, new(), manager, registry);
        var original = FakeManagedServerService.Server;
        var unverified = (await service.SourcesAsync(default)).Single();
        Assert.AreEqual(original.Name, unverified.Name);
        Assert.IsNull(unverified.LibraryEpoch);
        Assert.IsFalse(unverified.Available);
        await registry.PutAsync(new(original.ServerId, "verified-epoch", "Renamed source"), default);
        var verified = (await service.SourcesAsync(default)).Single();
        Assert.AreEqual(original.ServerId, verified.DeviceId);
        Assert.AreEqual("Renamed source", verified.Name);
        Assert.AreEqual("verified-epoch", verified.LibraryEpoch);
        Assert.AreEqual("managed", verified.Kind);
        Assert.IsTrue(verified.Available);
        await registry.PutAsync(new(original.ServerId, "verified-epoch", "Renamed source", Denied: true), default);
        var denied = (await service.SourcesAsync(default)).Single();
        Assert.IsNull(denied.LibraryEpoch);
        Assert.IsFalse(denied.Available);
        await registry.PutAsync(new(original.ServerId, "", "Old source", ErrorCode: "ManagementUpdateRequired"), default);
        Assert.IsNull((await service.SourcesAsync(default)).Single().LibraryEpoch);
        // Listing is local and cannot replace pairing credentials or path mappings.
        Assert.IsTrue(manager.Calls.All(c => c == "Get probe=False"));
        Assert.IsNull(manager.LastMappings);
    }

    private sealed class SourceDirectory : IFederationDataDirectory, INodeIdSource, IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "bakabase-profile-source-list-" + Guid.NewGuid().ToString("N"));
        public string Ensure() { Directory.CreateDirectory(Path); return Path; }
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("manager");
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }

    [TestMethod]
    public async Task ManagedSourceEvaluatesOnlySearchAndIdsAndUsesTheViewersArbitraryApplication()
    {
        var f = new Fixture(managed: true);
        f.Profiles.Items.Add(Profile(4, "private-windows-player.exe", priority: 1));
        f.Profiles.Items.Add(Profile(8, "another-local-application.exe", priority: 10));
        f.Matches = new() {Matches = [new() {ResourceId = 77, ProfileIds = [4, 8]}]};
        var result = await f.Service.ResolveAsync("source", "epoch", 77);
        Assert.AreEqual("another-local-application.exe", result.PlayerOptions!.Players!.Single().ExecutablePath);
        CollectionAssert.AreEqual(new[] {8, 4}, result.ProfileIds);
        var input = JsonConvert.DeserializeObject<ResourceProfileEvaluationInput>(f.EvaluationBody!)!;
        CollectionAssert.AreEqual(new[] {77}, input.ResourceIds);
        Assert.IsFalse(f.EvaluationBody!.Contains("exe"));
        Assert.IsFalse(f.EvaluationBody.Contains("private-argument"));
        Assert.AreEqual("epoch", f.ExpectedEpoch);
        Assert.AreEqual(0, f.Sessions.Calls);
    }

    [TestMethod]
    public async Task SourceDefaultApplicationBlockStopsLowerPriorityPlayerInheritance()
    {
        var f = new Fixture(managed: true);
        f.Profiles.Items.Add(Profile(4, "player.exe", 1));
        f.Profiles.Items.Add(Profile(8, null, 10));
        f.Matches = new() {Matches = [new() {ResourceId = 77, ProfileIds = [4, 8]}]};
        var result = await f.Service.ResolveAsync("source", "epoch", 77);
        Assert.IsNotNull(result.PlayerOptions);
        Assert.AreEqual(0, result.PlayerOptions.Players!.Count);
    }

    [TestMethod]
    public async Task SharedSourceUsesOnlyLibraryReadProtocolAndNeverSendsLocalPlayerOptions()
    {
        var f = new Fixture(managed: false);
        f.Profiles.Items.Add(Profile(4, "private-player.exe"));
        var result = await f.Service.ResolveAsync("source", "epoch", 77);
        Assert.AreEqual("private-player.exe", result.PlayerOptions!.Players!.Single().ExecutablePath);
        CollectionAssert.AreEqual(new[] {"/federation/v1/export/resource-profile/identity?libraryEpoch=epoch",
            "/federation/v1/export/resource-profile/evaluate?libraryEpoch=epoch"}, f.Transport.Paths.ToArray());
        Assert.IsFalse(f.EvaluationBody!.Contains("player.exe"));
        Assert.IsFalse(f.EvaluationBody.Contains("private-argument"));
    }

    [TestMethod]
    public async Task EmptyLocalProfileSetNeedsNoSourceNetwork()
    {
        var f = new Fixture(managed: false);
        var result = await f.Service.ResolveAsync("source", "epoch", 77);
        Assert.AreEqual(0, result.ProfileIds.Length);
        Assert.AreEqual(0, f.Sessions.Calls);
    }

    [TestMethod]
    [DataRow("previous")]
    [DataRow(null)]
    [DataRow("")]
    public async Task MissingOrReplacedLibraryIdentityNeverReusesAFilterAgainstNewResourceIds(string? epoch)
    {
        var f = new Fixture(managed: true);
        var profile = Profile(4, "player.exe");
        profile.SourceLibraryEpoch = epoch;
        f.Profiles.Items.Add(profile);
        var error = await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Service.ResolveAsync("source", "epoch", 77));
        Assert.AreEqual("ProfileSourceLibraryChanged", error.Code);
        Assert.IsNull(f.EvaluationBody);

    }

    [TestMethod]
    public async Task SourceIdentityCannotBeReplacedByAnotherDevicesAnswer()
    {
        var f = new Fixture(managed: true) {Info = new("impostor", "Other", "epoch")};
        f.Profiles.Items.Add(Profile(4, "player.exe"));
        Assert.AreEqual("InvalidProfileSource", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Service.ResolveAsync("source", "epoch", 77))).Code);
        Assert.IsNull(f.EvaluationBody);
    }

    [TestMethod]
    public async Task DefinitionOnlyGrantCannotReadLibrarySearchMetadata()
    {
        var f = new Fixture(managed: false);
        f.Sessions.Scope = FederationScopes.DataSyncRead;
        Assert.AreEqual("InvalidProfileSource", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Service.InfoAsync("source", "epoch", default))).Code);
        Assert.AreEqual(0, f.Transport.Paths.Count);
    }

    [TestMethod]
    [DataRow("unknown-profile")]
    [DataRow("unknown-resource")]
    [DataRow("duplicate-resource")]
    [DataRow("missing-resource")]
    public async Task ASourceCannotSelectProfilesOrResourcesOutsideTheSubmittedBatch(string invalid)
    {
        var f = new Fixture(managed: true);
        f.Profiles.Items.Add(Profile(4, "player.exe"));
        f.Matches = new() {Matches = invalid switch
        {
            "unknown-profile" => [new() {ResourceId = 77, ProfileIds = [999]}],
            "unknown-resource" => [new() {ResourceId = 88, ProfileIds = [4]}],
            "duplicate-resource" => [new() {ResourceId = 77, ProfileIds = [4]}, new() {ResourceId = 77, ProfileIds = [4]}],
            _ => []
        }};
        Assert.AreEqual(invalid == "missing-resource" ? "ResourceGone" : "InvalidProfileMatches", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            f.Service.ResolveAsync("source", "epoch", 77))).Code);
    }

    [TestMethod]
    public async Task BatchEvaluationIsOneSourceCallForAllRequestedResources()
    {
        var f = new Fixture(managed: true);
        f.Profiles.Items.Add(Profile(4, "player.exe"));
        f.Matches = new() {Matches = [new() {ResourceId = 77, ProfileIds = [4]}, new() {ResourceId = 78, ProfileIds = []}]};
        var result = await f.Service.ResolveManyAsync("source", "epoch", [77, 78]);
        Assert.AreEqual(1, f.Evaluations);
        Assert.AreEqual(1, result[77].ProfileIds.Length);
        Assert.AreEqual(0, result[78].ProfileIds.Length);
    }

    private static ResourceProfile Profile(int id, string? executable, int priority = 1) => new()
    {
        Id = id, Name = "Local profile", SourceDeviceId = "source", SourceLibraryEpoch = "epoch",
        Priority = priority, SearchJson = "{\"Keyword\":\"movie\"}", OpeningMode = "native",
        PlayerOptions = new() {Players = executable == null ? [] : [new() {ExecutablePath = executable, Command = "private-argument {0}"}]}
    };

    private sealed class Fixture
    {
        public readonly ProfileProxy Profiles;
        public readonly Sessions Sessions = new();
        public readonly Transport Transport;
        public readonly SourceResourceProfileService Service;
        public ResourceProfileSourceInfo Info = new("source", "Source", "epoch");
        public ResourceProfileEvaluationResult Matches = new() {Matches = [new() {ResourceId = 77, ProfileIds = [4]}]};
        public string? EvaluationBody;
        public string? ExpectedEpoch;
        public int Evaluations;

        public Fixture(bool managed)
        {
            var profileService = DispatchProxy.Create<IResourceProfileService, ProfileProxy>();
            Profiles = (ProfileProxy) profileService;
            Transport = new(this);
            IManagedServerService? manager = null;
            if (managed)
            {
                manager = DispatchProxy.Create<IManagedServerService, ManagedProxy>();
                ((ManagedProxy) manager).Read = (operation, json, epoch) =>
                {
                    ExpectedEpoch = epoch;
                    return Read(operation, json);
                };
            }
            Service = new(profileService, null!, Sessions, Transport, new GrantLeaseRegistry(), manager);
        }

        public string Read(string operation, string? json)
        {
            if (operation == "identity") return JsonConvert.SerializeObject(new SingletonResponse<ResourceProfileSourceInfo>(Info));
            Assert.AreEqual("evaluate", operation);
            Evaluations++;
            EvaluationBody = json;
            return JsonConvert.SerializeObject(new SingletonResponse<ResourceProfileEvaluationResult>(Matches));
        }
    }

    public class ProfileProxy : DispatchProxy
    {
        public List<ResourceProfile> Items = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != nameof(IResourceProfileService.GetAll)) throw new NotSupportedException(method.Name);
            var filter = (Expression<Func<ResourceProfileDbModel, bool>>?) args![0];
            return Task.FromResult(Items.Where(p => filter == null || filter.Compile()(new()
                {Id = p.Id, Name = p.Name, SourceDeviceId = p.SourceDeviceId})).ToList());
        }
    }

    public class ManagedProxy : DispatchProxy
    {
        public Func<string, string?, string?, string> Read = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            nameof(IManagedServerService.GetAsync) => Task.FromResult(new ManagedServersView(true,
                [FakeManagedServerService.Server with {ServerId = "source"}], [])),
            nameof(IManagedServerService.ReadResourceProfileSourceAsync) => Task.FromResult(Read((string) args![1]!,
                (string?) args[2], (string?) args[3])),
            _ => throw new NotSupportedException(method.Name)
        };
    }

    private sealed class Sessions : IPeerSessionFactory
    {
        public int Calls;
        public string Scope = FederationScopes.LibraryRead;
        public Task<PeerSessionSnapshot> GetAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new PeerSessionSnapshot(nodeId, "http://source.invalid",
                new("grant", "viewer", nodeId, "epoch", "secret", 1), TimeSpan.Zero,
                new(nodeId, "epoch", "Source", 1, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow, Scope));
        }
    }

    private sealed class Transport(Fixture owner) : INodeTransport
    {
        public readonly List<string> Paths = [];
        public Task<HttpResponseMessage> SendAsync(string nodeId, HttpMethod method, string relativePath,
            object? body = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<HttpResponseMessage> SendAsync(PeerSessionSnapshot peer, HttpMethod method, string relativePath,
            object? body = null, CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            Paths.Add(relativePath);
            var operation = relativePath.Split('/').Last().Split('?')[0];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {Content = new StringContent(owner.Read(operation, body?.ToString()))});
        }
    }
}
