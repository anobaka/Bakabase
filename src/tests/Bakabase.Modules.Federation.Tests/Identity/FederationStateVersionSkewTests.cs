using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Tests.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Identity;

/// <summary>
/// §13.8, the state file: a <c>state.json</c> this build wrote through real pairing — library and definitions grants
/// both ways, requests waiting both ways, a library code, a definitions code and a reciprocal code — checked in under
/// <c>Fixtures/VersionSkew</c>, then read with <c>FederationState</c>'s shape as a build from before data sync declares
/// it (§7.1.1). That build reads the file, sees its library access and nothing of definitions access, and a rewrite by
/// it drops definitions access, which then has to be approved again (§12). This build keeps reading the checked-in file
/// too. <c>DATASYNC_WRITE_FIXTURES=&lt;dir&gt;</c> writes it again (<see cref="WriteFixture"/>).
/// </summary>
[TestClass]
public sealed class FederationStateVersionSkewTests
{
    private const string FixtureName = "federation-state.json";

    /// <summary>What the older build's store used: <c>FederationStateStore</c>'s options before data sync.</summary>
    private static readonly JsonSerializerOptions OlderJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "VersionSkew", FixtureName);

    private static JsonObject Fixture() => JsonNode.Parse(File.ReadAllText(FixturePath))!.AsObject();

    [TestMethod]
    public void AnOlderBuildReadsThisBuildsStateAndSeesOnlyItsLibraryAccess()
    {
        var file = Fixture();
        var older = JsonSerializer.Deserialize<OlderFederationState>(File.ReadAllText(FixturePath), OlderJson)!;
        Assert.IsTrue(older.IsValid(), "the older build's own load check accepts it");
        Assert.AreEqual("source", older.NodeId);

        // Its library access, as the library collections hold it.
        var libraryGrant = older.InboundGrants.Values.Single();
        Assert.AreEqual("library-reader", libraryGrant.Credentials.SubjectNodeId);
        Assert.IsFalse(libraryGrant.Revoked);
        CollectionAssert.AreEqual(new[] { "library-reader" }, older.OutboundGrants.Keys.ToArray());
        Assert.IsTrue(older.SharingEnabled);
        Assert.IsNotNull(older.Invitation, "the library code");
        Assert.AreEqual(0, older.ReciprocalInvitations.Count);
        CollectionAssert.AreEquivalent(new[] { "library-asker" },
            older.IncomingRequests.Where(r => r.Status == "awaitingApproval").Select(r => r.NodeId).ToArray());

        // Nothing of definitions access: every datasync grant, request and code sits in members it does not have.
        var dataSyncGrantIds = ((JsonObject)file["inboundDataSyncGrants"]!).Select(g => g.Key).ToHashSet();
        Assert.AreEqual(1, dataSyncGrantIds.Count);
        Assert.IsFalse(older.InboundGrants.Keys.Any(dataSyncGrantIds.Contains));
        Assert.IsFalse(older.InboundGrants.Values.Any(g => g.Credentials.SubjectNodeId == "reader"));
        Assert.IsFalse(older.OutboundGrants.ContainsKey("reader"));
        var dataSyncRequests = ((JsonArray)file["incomingDataSyncRequests"]!)
            .Concat((JsonArray)file["outgoingDataSyncRequests"]!)
            .Select(r => (string)r!["requestId"]!).ToHashSet();
        Assert.AreEqual(4, dataSyncRequests.Count, "granted and waiting, each way");
        Assert.IsFalse(older.IncomingRequests.Select(r => r.RequestId)
            .Concat(older.OutgoingRequests.Select(r => r.RequestId)).Any(dataSyncRequests.Contains));
        Assert.IsNotNull(file["dataSyncInvitation"]);
        Assert.AreNotEqual((string?)file["dataSyncInvitation"]!["codeHash"], older.Invitation!.CodeHash);
        Assert.AreEqual(1, ((JsonArray)file["dataSyncReciprocalInvitations"]!).Count);
    }

    [TestMethod]
    public async Task AnOlderBuildsRewriteKeepsTheLibraryAccessAndDropsDefinitionsAccess()
    {
        var older = JsonSerializer.Deserialize<OlderFederationState>(File.ReadAllText(FixturePath), OlderJson)!;
        var libraryGrant = older.InboundGrants.Values.Single().Credentials;

        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        await File.WriteAllTextAsync(Path.Combine(source.Ensure(), FederationStateStore.FileName),
            JsonSerializer.Serialize(older, OlderJson));

        await source.Grants.ValidateAsync(libraryGrant.GrantId, libraryGrant.LibraryEpoch);
        var library = await source.Peers.GetStatusAsync();
        Assert.IsTrue(library.SharingEnabled);
        Assert.IsNotNull(library.Peers.Single(p => p.NodeId == "library-reader").InboundGrant);
        var status = await source.Peers.GetDataSyncStatusAsync();
        Assert.IsFalse(status.SharingEnabled, "the switch was a member it did not have");
        Assert.AreEqual(0, status.Grants.Count, "definitions access must be approved again");
        Assert.AreEqual(0, status.Requests.Count);
        Assert.IsFalse(status.Peers.Any(p => p.WeMayRead || p.TheyMayRead));
    }

    [TestMethod]
    public async Task ThisBuildStillReadsItsCheckedInState()
    {
        var file = Fixture();
        var libraryGrant = ((JsonObject)file["inboundGrants"]!).Single().Value!["credentials"]!;

        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        File.Copy(FixturePath, Path.Combine(source.Ensure(), FederationStateStore.FileName));

        await source.Grants.ValidateAsync((string)libraryGrant["grantId"]!, (string)libraryGrant["libraryEpoch"]!);
        var status = await source.Peers.GetDataSyncStatusAsync();
        Assert.IsTrue(status.SharingEnabled);
        Assert.AreEqual("reader", status.Grants.Single().NodeId);
        var reader = status.Peers.Single(p => p.NodeId == "reader");
        Assert.IsTrue(reader.WeMayRead && reader.TheyMayRead, "definitions access both ways");
        CollectionAssert.AreEquivalent(new[] { ("incoming", "asker"), ("outgoing", "other") },
            status.Requests.Where(r => r.Status == "awaitingApproval").Select(r => (r.Direction, r.NodeId)).ToArray());
        Assert.AreEqual("Asker", status.Requests.Single(r => r.NodeId == "asker").NodeName);
    }

    // ---- writing the fixture -----------------------------------------------------------------------

    [TestMethod]
    public async Task WriteFixtureWhenAsked()
    {
        if (Environment.GetEnvironmentVariable("DATASYNC_WRITE_FIXTURES") is not { Length: > 0 } dir)
        {
            Assert.IsTrue(File.Exists(FixturePath), "the fixture is copied next to the tests");
            return;
        }

        await WriteFixture(dir);
    }

    /// <summary>
    /// Writes <c>federation-state.json</c> into <paramref name="dir"/>: the state file of a device ("source") as this
    /// build leaves it after a person paired it every way there is. Its keys and codes are the test network's own.
    /// </summary>
    public static async Task WriteFixture(string dir)
    {
        using var network = new DataSyncTestNetwork();
        async Task<DataSyncTestNode> Add(string id, string name)
        {
            var node = network.Add(id);
            await node.Store.SetDisplayNameAsync(name);
            return node;
        }

        var source = await Add("source", "Source");
        var libraryReader = await Add("library-reader", "Library reader");
        var reader = await Add("reader", "Reader");
        var other = await Add("other", "Other");

        // Library access both ways, and a library request waiting for a person.
        await source.GrantLibraryAsync(libraryReader);
        await libraryReader.GrantLibraryAsync(source);
        Assert.AreEqual("awaitingApproval",
            (await (await Add("library-asker", "Library asker")).Pairing.ConnectAsync("http://source", null)).Outcome);

        // Definitions access both ways, a definitions request waiting each way, and every kind of code.
        await source.GrantDataSyncAsync(reader, NodeDataSyncIntents.TwoWay);
        await reader.GrantDataSyncAsync(source);
        await (await Add("asker", "Asker")).Pairing.ConnectDataSyncAsync("http://source", null,
            NodeDataSyncIntents.Follow, DataSyncTestNetwork.Contract);
        await other.Peers.SetDataSyncSharingAsync(true);
        await source.Pairing.ConnectDataSyncAsync("http://other", null, NodeDataSyncIntents.Follow,
            DataSyncTestNetwork.Contract);
        await source.Peers.IssueDataSyncInvitationAsync(allowTwoWay: true);
        await source.Peers.CreateDataSyncReciprocalInvitationAsync("reader");
        await source.Peers.IssueInvitationAsync();

        Directory.CreateDirectory(dir);
        var state = File.ReadAllText(Path.Combine(source.Path, FederationStateStore.FileName));
        await File.WriteAllTextAsync(Path.Combine(dir, FixtureName),
            JsonNode.Parse(state)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    // ---- FederationState as a build from before data sync declares it -----------------------------

    /// <summary>The members, types and load check of <c>FederationState</c> before data sync (§7.1.1).</summary>
    private sealed class OlderFederationState
    {
        public int SchemaVersion { get; set; } = 1;
        public string? NodeId { get; set; }
        public string? LibraryEpoch { get; set; }
        public bool SharingEnabled { get; set; }
        public bool BrowsingEnabled { get; set; }
        public Dictionary<string, OlderStoredPeer> Peers { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, OlderStoredGrant> InboundGrants { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, NodeCredentials> OutboundGrants { get; set; } = new(StringComparer.Ordinal);
        public List<OlderStoredPairRequest> IncomingRequests { get; set; } = [];
        public List<OlderStoredOutgoingRequest> OutgoingRequests { get; set; } = [];
        public OlderStoredInvitation? Invitation { get; set; }
        public List<OlderStoredReciprocalInvitation> ReciprocalInvitations { get; set; } = [];
        public string? DisplayName { get; set; }

        /// <summary>What its store checked before it used a file.</summary>
        public bool IsValid() =>
            SchemaVersion == 1 && (NodeId != null) == (LibraryEpoch != null) &&
            (NodeId == null || NodeRequestSignature.IsIdentifier(NodeId) &&
                NodeRequestSignature.IsIdentifier(LibraryEpoch!)) &&
            Peers != null && InboundGrants != null && OutboundGrants != null && IncomingRequests != null &&
            OutgoingRequests != null;
    }

    private sealed class OlderStoredReciprocalInvitation
    {
        public string CodeHash { get; set; } = "";
        public string AudienceNodeId { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed class OlderStoredPeer
    {
        public string NodeId { get; set; } = "";
        public string Label { get; set; } = "";
        public ServerKind? Kind { get; set; }
        public RemoteDevicePlatform? Platform { get; set; }
        public string? Address { get; set; }
        public string? LibraryEpoch { get; set; }
        public bool Enabled { get; set; } = true;
        public List<NodePathMapping> PathMappings { get; set; } = [];
    }

    private sealed class OlderStoredGrant
    {
        public NodeCredentials Credentials { get; set; } = null!;
        public bool Revoked { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class OlderStoredInvitation
    {
        public string CodeHash { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
        public int FailedAttempts { get; set; }
    }

    private sealed class OlderStoredPairRequest
    {
        public string RequestId { get; set; } = "";
        public string TransactionId { get; set; } = "";
        public string NodeId { get; set; } = "";
        public string NodeName { get; set; } = "";
        public string ClaimSecretHash { get; set; } = "";
        public string Status { get; set; } = "awaitingApproval";
        public DateTimeOffset ExpiresAt { get; set; }
        public string? GrantId { get; set; }
        public string? RemoteAddress { get; set; }
        public NodeReciprocalOffer? Reciprocal { get; set; }
    }

    private sealed class OlderStoredOutgoingRequest
    {
        public string RequestId { get; set; } = "";
        public string Address { get; set; } = "";
        public string NodeId { get; set; } = "";
        public string NodeName { get; set; } = "";
        public string LibraryEpoch { get; set; } = "";
        public string ClaimSecret { get; set; } = "";
        public string Status { get; set; } = "awaitingApproval";
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
