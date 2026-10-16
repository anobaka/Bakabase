using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

[TestClass]
public sealed class ConsolePathMappingEndpointsTests
{
    private ConsoleHarness _console = null!;
    private FakeServer _owner = null!;
    private FakeServer _other = null!;
    private int _port;
    private string Target => $"/client/servers/{_owner.ServerId}";

    [TestInitialize]
    public async Task Setup()
    {
        _console = await ConsoleHarness.StartAsync();
        _owner = await FakeServer.StartAsync("mapping-owner", "Owner", 46900);
        _other = await FakeServer.StartAsync("mapping-other", "Other", 46900);
        await _console.AddManagedAsync(_owner);
        await _console.AddManagedAsync(_other);
        _port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_owner.ServerId, null))!.Url);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();
        await _owner.DisposeAsync();
        await _other.DisposeAsync();
    }

    [TestMethod]
    public async Task Recovered_relay_draft_cannot_replace_a_newer_host_editor_save()
    {
        await _console.Manager.SetPathMappingsAsync(_owner.ServerId, [new("/owner", "/local")]);
        using var stale = await ConsoleHarness.SendToRelayAsync(_port, Target + "/path-mappings", HttpMethod.Put,
            "{\"mappings\":[{\"serverPath\":\"/stale\",\"localPath\":\"/old\"}],\"expectedMappings\":[]}");
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        StringAssert.Contains(await stale.Content.ReadAsStringAsync(), "PathMappingsChanged");
        Assert.AreEqual("/owner", _console.Store.Find(_owner.ServerId)!.PathMappings.Single().ServerPath);

        using var current = await ConsoleHarness.SendToRelayAsync(_port, Target + "/path-mappings", HttpMethod.Put,
            "{\"mappings\":[],\"expectedMappings\":[{\"serverPath\":\"/owner\",\"localPath\":\"/local\"}]}");
        Assert.AreEqual(HttpStatusCode.OK, current.StatusCode);
        Assert.AreEqual(0, _console.Store.Find(_owner.ServerId)!.PathMappings.Count);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("not json")]
    [DataRow("{\"mappings\":[null]}")]
    [DataRow("{\"mappings\":[],\"expectedMappings\":[null]}")]
    public async Task Invalid_save_never_erases_existing_mappings(string input)
    {
        await _console.Manager.SetPathMappingsAsync(_owner.ServerId, [new("/owner", "/local")]);
        using var response = await ConsoleHarness.SendToRelayAsync(_port, Target + "/path-mappings", HttpMethod.Put, input);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("/local", _console.Store.Find(_owner.ServerId)!.PathMappings.Single().LocalPath);
    }

    [TestMethod]
    public async Task Tree_reads_only_current_owners_metadata_through_a_signed_connection()
    {
        _owner.HandleRequest = async context =>
        {
            if (context.Request.Path != "/management/library/path-mapping-tree") return false;
            Assert.AreEqual("/owner/media", context.Request.Query["parent"].ToString());
            Assert.AreEqual("200", context.Request.Query["limit"].ToString());
            await context.Response.WriteAsJsonAsync(new {libraryEpoch = "epoch-one", nodes = new[]
                {new {serverPath = "/owner/media/videos", name = "videos", resourceCount = 1234567L, hasChildren = true}}, nextCursor = "next_token"});
            return true;
        };
        using var response = await ConsoleHarness.SendToRelayAsync(_port,
            Target + "/path-mapping-tree?parent=%2Fowner%2Fmedia&libraryEpoch=epoch-one");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual("/owner/media/videos", body.GetProperty("data").GetProperty("nodes")[0].GetProperty("serverPath").GetString());
        Assert.IsTrue(_owner.Requests.Single(r => r.Path == "/management/library/path-mapping-tree").SignatureValid);

        var requests = _other.Requests.Count;
        using var refused = await ConsoleHarness.SendToRelayAsync(_port,
            $"/client/servers/{_other.ServerId}/path-mapping-tree");
        Assert.AreEqual(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.AreEqual(requests, _other.Requests.Count);
    }

    [TestMethod]
    [DataRow("limit=201")]
    [DataRow("limit=0")]
    [DataRow("limit=invalid")]
    [DataRow("parent=%2Fowner%2F..%2Fsecret")]
    [DataRow("cursor=invalid%3F")]
    [DataRow("limit=1&limit=2")]
    [DataRow("unknown=ignored")]
    public async Task Invalid_or_unbounded_tree_requests_never_reach_the_owner(string query)
    {
        using var response = await ConsoleHarness.SendToRelayAsync(_port, Target + "/path-mapping-tree?" + query);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.IsFalse(_owner.Requests.Any(r => r.Path == "/management/library/path-mapping-tree"));
    }

    [TestMethod]
    public async Task Tree_rejects_epoch_change_and_oversized_owner_page()
    {
        _owner.HandleRequest = async context =>
        {
            if (context.Request.Path != "/management/library/path-mapping-tree") return false;
            await context.Response.WriteAsJsonAsync(new {libraryEpoch = "new-epoch", nodes = Array.Empty<object>()});
            return true;
        };
        using var changed = await ConsoleHarness.SendToRelayAsync(_port,
            Target + "/path-mapping-tree?libraryEpoch=old-epoch");
        Assert.AreEqual(HttpStatusCode.Conflict, changed.StatusCode);
        StringAssert.Contains(await changed.Content.ReadAsStringAsync(), "LibraryEpochChanged");

        _owner.HandleRequest = async context =>
        {
            if (context.Request.Path != "/management/library/path-mapping-tree") return false;
            await context.Response.WriteAsJsonAsync(new {libraryEpoch = "new-epoch", nodes = Enumerable.Range(0, 201)
                .Select(i => new {serverPath = "/owner/" + i, name = i.ToString(), resourceCount = 1, hasChildren = false})});
            return true;
        };
        using var tooMany = await ConsoleHarness.SendToRelayAsync(_port, Target + "/path-mapping-tree");
        Assert.AreEqual(HttpStatusCode.BadGateway, tooMany.StatusCode);
        StringAssert.Contains(await tooMany.Content.ReadAsStringAsync(), "InvalidMappingTreeResponse");
    }

    [TestMethod]
    public async Task Tree_refuses_an_oversized_chunked_body_without_buffering_the_owner_response()
    {
        _owner.HandleRequest = async context =>
        {
            if (context.Request.Path != "/management/library/path-mapping-tree") return false;
            context.Response.ContentType = "application/json";
            // Unknown Content-Length: the relay must enforce the budget while reading too.
            await context.Response.WriteAsync("{\"libraryEpoch\":\"epoch\",\"nodes\":[],\"unexpected\":\"");
            var chunk = new string('x', 8192);
            for (var i = 0; i < 130; i++) await context.Response.WriteAsync(chunk);
            await context.Response.WriteAsync("\"}");
            return true;
        };
        using var response = await ConsoleHarness.SendToRelayAsync(_port, Target + "/path-mapping-tree");
        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "InvalidMappingTreeResponse");
    }
}
