using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Identity;

/// <summary>
/// <c>state.json</c> across the data sync members, both ways: this build reads a file written before
/// them, and a build from before them, reading a file written now, never takes datasync access for
/// library access (it ignores unknown members, so the separate collections are invisible to it).
/// </summary>
[TestClass]
public sealed class FederationStateCompatibilityTests
{
    private static readonly string[] DataSyncCollections =
    [
        "inboundDataSyncGrants", "outboundDataSyncGrants", "incomingDataSyncRequests", "outgoingDataSyncRequests",
        "dataSyncReciprocalInvitations"
    ];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AFileWithoutDataSyncStateLoadsWithItsLibraryStateAndStaysWritable(bool collectionsAsNull)
    {
        using var node = new Node();
        var grant = await node.GrantAsync();
        var file = node.ReadFile();
        file.Remove("dataSyncSharingEnabled");
        file.Remove("dataSyncInvitation");
        foreach (var member in DataSyncCollections)
            if (collectionsAsNull) file[member] = null;
            else file.Remove(member);
        node.WriteFile(file);

        using var restarted = node.Restart();
        var status = await restarted.Peers.GetStatusAsync();
        Assert.IsTrue(status.SharingEnabled);
        Assert.AreEqual(grant.GrantId, status.Peers.Single().InboundGrant?.GrantId);
        await restarted.Grants.ValidateAsync(grant.GrantId, grant.LibraryEpoch);

        await restarted.Store.SetDisplayNameAsync("Renamed");
        var rewritten = node.ReadFile();
        Assert.IsFalse(rewritten["dataSyncSharingEnabled"]!.GetValue<bool>());
        foreach (var member in DataSyncCollections)
            Assert.AreEqual(0, rewritten[member] switch
            {
                JsonObject o => o.Count,
                JsonArray a => a.Count,
                _ => -1
            }, member);
    }

    [TestMethod]
    public async Task DataSyncStateIsKeptApartFromLibraryStateAndInvisibleToAnOlderBuild()
    {
        using var node = new Node();
        var libraryGrant = await node.GrantAsync();
        var local = await node.Identity.GetAsync();
        var expires = node.Clock.GetUtcNow().AddMinutes(10);

        // What datasync approvals, requests and codes store, written as this build writes them.
        var file = node.ReadFile();
        file["peers"]!["reader-d"] = new JsonObject
        {
            ["nodeId"] = "reader-d", ["label"] = "Reader D", ["address"] = "http://192.168.20.9:5000",
            ["libraryEpoch"] = "reader-d-epoch", ["enabled"] = true, ["pathMappings"] = new JsonArray(),
            ["dataSyncAddress"] = "http://192.168.20.9:6000"
        };
        file["dataSyncSharingEnabled"] = true;
        file["inboundDataSyncGrants"] = new JsonObject
        {
            ["datasync-grant"] = new JsonObject
            {
                ["credentials"] = Credentials("datasync-grant", "reader-d", local.NodeId, local.LibraryEpoch),
                ["revoked"] = false, ["createdAt"] = node.Clock.GetUtcNow()
            }
        };
        file["outboundDataSyncGrants"] = new JsonObject
            { ["reader-d"] = Credentials("datasync-outbound", local.NodeId, "reader-d", "reader-d-epoch") };
        file["incomingDataSyncRequests"] = new JsonArray(new JsonObject
        {
            ["requestId"] = "datasync-request", ["transactionId"] = "datasync-request", ["nodeId"] = "reader-e",
            ["nodeName"] = "Reader E", ["claimSecretHash"] = "hash", ["status"] = "awaitingApproval",
            ["expiresAt"] = expires, ["intent"] = "twoWay"
        });
        file["outgoingDataSyncRequests"] = new JsonArray(new JsonObject
        {
            ["requestId"] = "datasync-outgoing", ["address"] = "http://192.168.20.10:5000", ["nodeId"] = "source-f",
            ["nodeName"] = "Source F", ["libraryEpoch"] = "source-f-epoch", ["claimSecret"] = "secret",
            ["status"] = "awaitingApproval", ["expiresAt"] = expires, ["intent"] = "follow"
        });
        file["dataSyncInvitation"] = new JsonObject
            { ["codeHash"] = "hash", ["expiresAt"] = expires, ["failedAttempts"] = 0, ["allowTwoWay"] = true };
        file["dataSyncReciprocalInvitations"] = new JsonArray(new JsonObject
            { ["codeHash"] = "hash", ["audienceNodeId"] = "reader-d", ["expiresAt"] = expires });
        node.WriteFile(file);

        // This build keeps every one of them through a rewrite: the names above are the ones it stores.
        using var restarted = node.Restart();
        await restarted.Store.SetDisplayNameAsync("Renamed");
        var rewritten = node.ReadFile();
        Assert.IsTrue(rewritten["dataSyncSharingEnabled"]!.GetValue<bool>());
        Assert.AreEqual("http://192.168.20.9:6000", (string?)rewritten["peers"]!["reader-d"]!["dataSyncAddress"]);
        Assert.AreEqual("datasync-grant",
            (string?)rewritten["inboundDataSyncGrants"]!["datasync-grant"]!["credentials"]!["grantId"]);
        Assert.AreEqual("datasync-outbound", (string?)rewritten["outboundDataSyncGrants"]!["reader-d"]!["grantId"]);
        Assert.AreEqual("twoWay", (string?)rewritten["incomingDataSyncRequests"]![0]!["intent"]);
        Assert.AreEqual("follow", (string?)rewritten["outgoingDataSyncRequests"]![0]!["intent"]);
        Assert.IsTrue(rewritten["dataSyncInvitation"]!["allowTwoWay"]!.GetValue<bool>());
        Assert.AreEqual("reader-d", (string?)rewritten["dataSyncReciprocalInvitations"]![0]!["audienceNodeId"]);

        // Library views and library authentication never see them.
        var status = await restarted.Peers.GetStatusAsync();
        var readerD = status.Peers.Single(p => p.NodeId == "reader-d");
        Assert.IsNull(readerD.InboundGrant);
        Assert.IsNull(readerD.OutboundGrant);
        Assert.IsFalse(status.Requests.Any(r => r.RequestId.StartsWith("datasync-")));
        // Refused either way: unknown to library validation today, ScopeNotGranted once scopes are checked (G28).
        var datasyncAsLibrary = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            restarted.Grants.ValidateAsync("datasync-grant", local.LibraryEpoch));
        CollectionAssert.Contains(new[] { "GrantRevoked", "ScopeNotGranted" }, datasyncAsLibrary.ErrorCode);
        await restarted.Grants.ValidateAsync(libraryGrant.GrantId, libraryGrant.LibraryEpoch);

        // Nor does a build from before data sync.
        var older = JsonSerializer.Deserialize<PreDataSyncState>(rewritten.ToJsonString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        CollectionAssert.AreEquivalent(new[] { libraryGrant.GrantId }, older.InboundGrants.Keys.ToArray());
        Assert.AreEqual(0, older.OutboundGrants.Count);
        Assert.IsFalse(older.IncomingRequests.Concat(older.OutgoingRequests)
            .Any(r => r.GetProperty("requestId").GetString()!.StartsWith("datasync-")));
        Assert.IsNull(older.Invitation);
        Assert.AreEqual(0, older.ReciprocalInvitations.Count);
    }

    private static JsonObject Credentials(string grantId, string subject, string audience, string epoch) => new()
    {
        ["grantId"] = grantId, ["subjectNodeId"] = subject, ["audienceNodeId"] = audience, ["libraryEpoch"] = epoch,
        ["key"] = NodeRequestSignature.RandomToken(), ["revision"] = 1
    };

    /// <summary><c>FederationState</c>'s members as a build from before data sync declares them.</summary>
    private sealed class PreDataSyncState
    {
        public int SchemaVersion { get; set; } = 1;
        public string? NodeId { get; set; }
        public string? LibraryEpoch { get; set; }
        public bool SharingEnabled { get; set; }
        public bool BrowsingEnabled { get; set; }
        public Dictionary<string, JsonElement> Peers { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> InboundGrants { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> OutboundGrants { get; set; } = new(StringComparer.Ordinal);
        public List<JsonElement> IncomingRequests { get; set; } = [];
        public List<JsonElement> OutgoingRequests { get; set; } = [];
        public JsonElement? Invitation { get; set; }
        public List<JsonElement> ReciprocalInvitations { get; set; } = [];
        public string? DisplayName { get; set; }
    }

    private sealed class Node : IDisposable
    {
        private readonly bool _ownsDirectory;
        public DataDirectory Folder { get; }
        public IdSource Source { get; }
        public Clock Clock { get; }
        public GrantLeaseRegistry Leases { get; } = new();
        public FederationStateStore Store { get; }
        public INodeIdentityProvider Identity { get; }
        public FederationPeerService Peers { get; }
        public NodeGrantService Grants { get; }

        public Node() : this(new DataDirectory(), new IdSource(), new Clock(), ownsDirectory: true) { }

        private Node(DataDirectory folder, IdSource source, Clock clock, bool ownsDirectory)
        {
            _ownsDirectory = ownsDirectory;
            Folder = folder;
            Source = source;
            Clock = clock;
            Store = new(Folder, Source);
            Identity = new NodeIdentityProvider(Store);
            Peers = new(Store, Identity, Leases, Clock);
            Grants = new(Store, Identity, Leases, Clock);
        }

        /// <summary>The same data directory opened again, as after a restart.</summary>
        public Node Restart() => new(Folder, Source, Clock, ownsDirectory: false);

        public async Task<NodeCredentials> GrantAsync()
        {
            await Peers.SetSharingAsync(true);
            var request = new NodePairRequest("reader-a", "Reader A", NodeRequestSignature.RandomToken(18),
                NodeRequestSignature.RandomToken());
            await Peers.RequestPairingAsync(request);
            await Peers.ApproveAsync(request.TransactionId);
            return (await Peers.ClaimPairingAsync(new(request.TransactionId, request.NodeId, request.ClaimSecret)))
                .Credentials!;
        }

        private string FilePath => System.IO.Path.Combine(Folder.Path, FederationStateStore.FileName);
        public JsonObject ReadFile() => JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        public void WriteFile(JsonObject state) => File.WriteAllText(FilePath, state.ToJsonString());

        public void Dispose()
        {
            Leases.Dispose();
            if (_ownsDirectory && Directory.Exists(Folder.Path)) Directory.Delete(Folder.Path, true);
        }
    }

    private sealed class DataDirectory : IFederationDataDirectory
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "bakabase-node-state-tests-" + Guid.NewGuid().ToString("N"));
        public string Ensure()
        {
            Directory.CreateDirectory(Path);
            return Path;
        }
    }

    private sealed class IdSource : INodeIdSource
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(Id);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
