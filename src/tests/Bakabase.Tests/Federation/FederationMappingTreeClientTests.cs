using System.Net;
using System.Text.Json;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Service.Components.Federation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationMappingTreeClientTests
{
    [TestMethod]
    public async Task SelectsOnlyTheExplicitPeerAndPinsItsLibraryGrant()
    {
        using var leases = new GrantLeaseRegistry();
        var sessions = new Sessions();
        var transport = new Transport();
        var client = new FederationMappingTreeClient(sessions, transport, leases);
        await client.ReadAsync("peer-one", "opaque-parent", "opaque-cursor", 2, default);
        await client.ReadAsync("peer-two", null, null, 1, default);
        CollectionAssert.AreEqual(new[] { "peer-one", "peer-two" }, sessions.Requested.ToArray());
        CollectionAssert.AreEqual(new[] { "peer-one", "peer-two" }, transport.Targets.ToArray());
        Assert.AreEqual("/federation/v1/export/mapping-tree?limit=2&parent=opaque-parent&cursor=opaque-cursor", transport.Paths[0]);
        Assert.AreEqual("/federation/v1/export/mapping-tree?limit=1", transport.Paths[1]);
        Assert.IsTrue(transport.Methods.All(x => x == HttpMethod.Get));
    }

    [TestMethod]
    public async Task WrongIdentityOrDefinitionsSessionCannotReadTree()
    {
        using var leases = new GrantLeaseRegistry();
        var sessions = new Sessions();
        var transport = new Transport();
        var client = new FederationMappingTreeClient(sessions, transport, leases);
        sessions.WrongNode = true;
        Assert.AreEqual("InvalidPeerResponse", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            client.ReadAsync("peer-one", null, null, 200, default))).Code);
        sessions.WrongNode = false;
        sessions.Scope = FederationScopes.DataSyncRead;
        Assert.AreEqual("InvalidPeerResponse", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            client.ReadAsync("peer-one", null, null, 200, default))).Code);
        Assert.AreEqual(0, transport.Paths.Count);
    }

    [TestMethod]
    public async Task InvalidInputIsRejectedBeforeSessionLookupAndOldPeerHasExplicitFallback()
    {
        using var leases = new GrantLeaseRegistry();
        var sessions = new Sessions();
        var transport = new Transport { Status = HttpStatusCode.NotFound };
        var client = new FederationMappingTreeClient(sessions, transport, leases);
        foreach (var node in new[] { "http://other.invalid", "../peer", new string('x',129) })
            Assert.AreEqual("InvalidMappingTreeRequest", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
                client.ReadAsync(node, null, null, 200, default))).Code);
        Assert.AreEqual(0, sessions.Requested.Count);
        Assert.AreEqual("MappingTreeUnavailable", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            client.ReadAsync("peer-one", null, null, 200, default))).Code);
    }

    [TestMethod]
    public async Task PeerCannotReturnUnboundedOrMalformedTreeNodes()
    {
        using var leases = new GrantLeaseRegistry();
        var transport = new Transport();
        var client = new FederationMappingTreeClient(new Sessions(), transport, leases);
        foreach (var result in new[]
                 {
                     new MappingTreePage([new("legacy","Node",1,false,"token")]),
                     new MappingTreePage([new("g"+new string('a',64),"Node",-1,false,"token")]),
                     new MappingTreePage([new("g"+new string('a',64),"Node",1,false,"/absolute-path")]),
                     new MappingTreePage(Enumerable.Range(0,201).Select(x => new MappingTreeNode("g"+x.ToString().PadLeft(64,'0'),"Node",1,false,"token")).ToArray())
                 })
        {
            transport.Result = result;
            Assert.AreEqual("InvalidPeerResponse", (await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
                client.ReadAsync("peer-one",null,null,200,default))).Code);
        }
    }

    private sealed class Sessions : IPeerSessionFactory
    {
        public readonly List<string> Requested = [];
        public bool WrongNode;
        public string Scope = FederationScopes.LibraryRead;
        public Task<PeerSessionSnapshot> GetAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            Requested.Add(nodeId);
            var actual = WrongNode ? "different-peer" : nodeId;
            return Task.FromResult(new PeerSessionSnapshot(actual,"http://peer.invalid",new("grant-"+actual,"self",actual,"epoch", "unused",1),
                TimeSpan.Zero,new(actual,"epoch","Peer",1,DateTimeOffset.UtcNow),DateTimeOffset.UtcNow,Scope));
        }
    }
    private sealed class Transport : INodeTransport
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public MappingTreePage Result = new([]);
        public readonly List<string> Targets = [];
        public readonly List<string> Paths = [];
        public readonly List<HttpMethod> Methods = [];
        public Task<HttpResponseMessage> SendAsync(string nodeId, HttpMethod method, string relativePath, object? body = null, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The request must use a pinned verified session.");
        public Task<HttpResponseMessage> SendAsync(PeerSessionSnapshot session, HttpMethod method, string relativePath, object? body = null,
            CancellationToken cancellationToken = default, IReadOnlyDictionary<string,string>? headers = null)
        {
            Targets.Add(session.NodeId); Paths.Add(relativePath); Methods.Add(method);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(Result,FederationJson.Options)) });
        }
    }
}
