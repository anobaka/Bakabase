using System.Net;
using System.Reflection;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests.Federation;

[TestClass]
public class ManagedLibraryMappingControllerTests
{
    private const string Epoch = "library-one";
    private static (IManagedServerService, ManagedLibraryTests.ManagementWire) Wire()
    {
        var managed = DispatchProxy.Create<IManagedServerService, ManagedLibraryTests.ManagementWire>();
        var wire = (ManagedLibraryTests.ManagementWire)(object)managed;
        wire.WriteResponse = new(200, "application/json", JsonConvert.SerializeObject(
            new ManagedLibraryMappingTreePage(Epoch, [new("/Volumes/nas", "nas", 20, true)], null)));
        return (managed, wire);
    }

    private static Task<ServiceGateHost> Host(IManagedServerService managed) =>
        ServiceGateHost.StartAsync([typeof(ManagedServerMappingTreeController)], services =>
        {
            services.AddSingleton(managed);
            services.AddSingleton<INodeIdentityProvider>(new ManagedLibraryTests.Identity());
            services.AddSingleton<IServerSelfDescription>(new ServerSelfDescription(() => ServerKind.Headless));
            services.AddScoped<ManagedLibraryService>();
        });

    [TestMethod]
    public async Task Management_tree_uses_paired_owner_operation_and_returns_real_path_and_epoch()
    {
        var (managed, wire) = Wire();
        await using var host = await Host(managed);
        using var response = await host.SendAsync(HttpMethod.Get,
            "/federation/local/servers/source-a/path-mapping-tree?parent=%2FVolumes%2Fnas&limit=5&libraryEpoch=library-one",
            "same-origin", host.Origin);
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        var page = JObject.Parse(body);
        Assert.AreEqual(Epoch, page.GetValue("LibraryEpoch", StringComparison.OrdinalIgnoreCase)!.Value<string>());
        var call = wire.Calls.Last();
        Assert.AreEqual("source-a", call.ServerId);
        StringAssert.StartsWith(call.Path, "/management/library/path-mapping-tree?");
        StringAssert.Contains(call.Path, "parent=%2FVolumes%2Fnas");
        StringAssert.Contains(call.Path, "limit=5");
        StringAssert.Contains(call.Path, "managedLibraryEpoch=library-one");
        Assert.IsTrue(wire.Calls.All(c => c.Method == HttpMethod.Get));
    }

    [TestMethod]
    public async Task Headless_own_origin_can_read_tree_but_cross_site_cannot()
    {
        var (managed, wire) = Wire();
        await using var host = await Host(managed);
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        var path = "/federation/local/servers/source-a/path-mapping-tree";
        using var own = host.Request(HttpMethod.Get, path, "same-origin", host.Origin);
        own.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.1.5");
        using var allowed = await host.SendAsync(own);
        Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode, await allowed.Content.ReadAsStringAsync());
        var before = wire.Calls.Count;
        using var denied = await host.SendAsync(HttpMethod.Get, path, "cross-site", "https://other.invalid");
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.AreEqual(before, wire.Calls.Count);
    }

    [TestMethod]
    public async Task Stale_epoch_never_requests_a_directory_page_from_replaced_library()
    {
        var (managed, wire) = Wire();
        await using var host = await Host(managed);
        using var response = await host.SendAsync(HttpMethod.Get,
            "/federation/local/servers/source-a/path-mapping-tree?libraryEpoch=old-library",
            "same-origin", host.Origin);
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.IsTrue(wire.Calls.All(c => c.Path == "/management/library/info"));
    }

    [TestMethod]
    public async Task Older_owner_reports_update_requirement_without_returning_fake_empty_tree()
    {
        var (managed, wire) = Wire();
        wire.WriteResponse = new(404, "application/json", "{}");
        await using var host = await Host(managed);
        using var response = await host.SendAsync(HttpMethod.Get,
            "/federation/local/servers/source-a/path-mapping-tree", "same-origin", host.Origin);
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "ManagementUpdateRequired");
    }

    [TestMethod]
    public async Task Invalid_tree_requests_keep_the_federation_error_shape_without_contacting_owner()
    {
        var (managed, wire) = Wire();
        await using var host = await Host(managed);
        foreach (var query in new[] { "limit=201", "parent=%2Fmedia%2F..%2Fprivate", "cursor=invalid%2Btoken" })
        {
            using var response = await host.SendAsync(HttpMethod.Get,
                "/federation/local/servers/source-a/path-mapping-tree?" + query,
                "same-origin", host.Origin);
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode,
                await response.Content.ReadAsStringAsync());
            Assert.AreEqual("InvalidMappingTreeRequest", JObject.Parse(await response.Content.ReadAsStringAsync())["code"]!.Value<string>());
        }
        Assert.AreEqual(0, wire.Calls.Count);
    }

    [TestMethod]
    public void Directory_pages_are_bounded_and_cannot_override_owner_identity()
    {
        var good = ManagedLibraryOperations.Build(new(Epoch, "resourcePathMappingTree",
            Query: new() { ["parent"] = ["/media"], ["limit"] = ["200"] }));
        StringAssert.Contains(good.Path, "limit=200");
        foreach (var query in new Dictionary<string, string[]>[]
                 {
                     new() { ["limit"] = ["201"] }, new() { ["limit"] = ["0"] },
                     new() { ["managedLibraryEpoch"] = ["other"] }, new() { ["includeRemote"] = ["true"] },
                     new() { ["parent"] = ["/media", "/elsewhere"] }
                 })
            Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(
                new(Epoch, "resourcePathMappingTree", Query: query)));
    }
}
