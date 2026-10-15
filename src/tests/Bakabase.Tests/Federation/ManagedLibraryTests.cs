using System.Collections.Concurrent;
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests.Federation;

[TestClass]
public class ManagedLibraryTests
{
    private const string Epoch = "library-one";
    private static ManagedLibraryRequest Search(int size = 24) => new(Epoch, "resourceSearch",
        Body: JObject.FromObject(new {page = 1, pageSize = size, keyword = "film"}));

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public void Custom_property_catalog_reads_accept_their_own_defined_additional_items(int flags)
    {
        foreach (var operation in new[] {"customProperties", "getCustomPropertyByKeys"})
        {
            var result = ManagedLibraryOperations.Build(new(Epoch, operation, Ids: [7],
                Query: new() {{"additionalItems", [flags.ToString()]}}));
            StringAssert.Contains(result.Path, "additionalItems=" + flags);
            StringAssert.Contains(result.Path, "managedLibraryEpoch=" + Epoch);
            Assert.AreEqual(HttpMethod.Get, result.Method);
        }
        Assert.AreEqual(2, (int)Bakabase.InsideWorld.Models.Constants.AdditionalItems.CustomPropertyAdditionalItem.ValueCount,
            "The editor requests the custom-property enum, not resource metadata bits.");
    }

    [DataTestMethod]
    [DataRow("1")]
    [DataRow("3")]
    [DataRow("4")]
    [DataRow("-1")]
    [DataRow("2147483647")]
    [DataRow("not-a-flag")]
    public void Custom_property_catalog_reads_reject_retired_or_unknown_additional_items(string flags)
    {
        foreach (var operation in new[] {"customProperties", "getCustomPropertyByKeys"})
            Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(
                new(Epoch, operation, Ids: [7], Query: new() {{"additionalItems", [flags]}})));
    }

    [TestMethod]
    public void Definition_and_collection_page_operations_remain_named_epoch_bound_and_bounded()
    {
        var extension = ManagedLibraryOperations.Build(new(Epoch, "extensionGroupPut", Id: 7,
            Body: JObject.FromObject(new {name = "Videos", extensions = new[] {".mkv"}})));
        Assert.AreEqual(HttpMethod.Put, extension.Method);
        Assert.AreEqual("/extension-group/7?managedLibraryEpoch=library-one", extension.Path);
        var members = ManagedLibraryOperations.Build(new(Epoch, "getCollectionMemberships", Id: 7, Ids: [8, 9]));
        Assert.AreEqual("/collection/7/memberships?resourceIds=8&resourceIds=9&managedLibraryEpoch=library-one", members.Path);
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(
            new(Epoch, "getCollectionMemberships", Id: 7, Ids: Enumerable.Range(1, 101).ToArray())));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(
            new(Epoch, "searchCollectionMembers", Id: 7, Query: new() {{"pageSize", ["100000"]}})));
        var gone = ManagedLibraryOperations.Build(new(Epoch, "searchCollectionMembers", Id: 7,
            Query: new() {{"filter", ["5"]}, {"pageIndex", ["2"]}, {"pageSize", ["40"]}}));
        StringAssert.Contains(gone.Path, "filter=5&pageIndex=2&pageSize=40");
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(
            new(Epoch, "searchCollectionMembers", Id: 7, Query: new() {{"filter", ["6"]}})));
        var ignored = ManagedLibraryOperations.Build(new(Epoch, "setCollectionMemberIgnored", Id: 7,
            Query: new() {{"resourceId", ["8"]}, {"ignored", ["true"]}}));
        Assert.AreEqual("/collection/7/members/8/ignored?ignored=true&managedLibraryEpoch=library-one", ignored.Path);
        Assert.IsNull(ignored.Json);
        var sorting = ManagedLibraryOperations.Build(new(Epoch, "sortCustomProperties",
            Body: JObject.FromObject(new {ids = Enumerable.Range(1, 200).ToArray()})));
        Assert.AreEqual("/custom-property/order?managedLibraryEpoch=library-one", sorting.Path);
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new(Epoch, "sortCustomProperties",
            Body: JObject.FromObject(new {ids = new[] {7, 7}}))));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new(Epoch, "extensionGroups",
            Query: new() {{"serverId", ["different-owner"]}})));
    }

    [DataTestMethod]
    [DataRow("/gui/url")]
    [DataRow("fileDelete")]
    [DataRow("resourcePlay")]
    [DataRow("https://example.com")]
    [DataRow("libraryReplaceAll")]
    [DataRow("patchResourceDisplayOptions")]
    [DataRow("patchUiOptions")]
    public void Only_named_library_operations_are_allowed(string operation)
    {
        var error = Assert.ThrowsExactly<FederationAccessException>(() =>
            ManagedLibraryOperations.Build(new(Epoch, operation)));
        Assert.AreEqual("InvalidManagedOperation", error.ErrorCode);
    }

    [TestMethod]
    public void Owner_display_options_are_epoch_bound_and_only_priority_can_be_patched()
    {
        var operation = ManagedLibraryOperations.Build(new(Epoch, "resourceDisplayOptions"));
        Assert.AreEqual(HttpMethod.Get, operation.Method);
        Assert.AreEqual("/federation/local/management/resource-display-options?managedLibraryEpoch=library-one", operation.Path);
        Assert.IsNull(operation.Json);
        Assert.IsTrue(FederationRoutePolicy.Allows(FederationEndpointKind.Local, "GET", "/federation/local/management/resource-display-options"));
        Assert.IsTrue(FederationRoutePolicy.Allows(FederationEndpointKind.Local, "PATCH", "/federation/local/management/resource-display-options"));
        Assert.IsFalse(FederationRoutePolicy.Allows(FederationEndpointKind.Local, "PUT", "/federation/local/management/resource-display-options"));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new(Epoch,
            "resourceDisplayOptionsPatch", Body: JObject.FromObject(new {uiResource = new {pageSize = 100}}))));
        var patch = ManagedLibraryOperations.Build(new(Epoch, "resourceDisplayOptionsPatch",
            Body: JObject.FromObject(new {propertyValueScopePriority = new[] {0, 1}})));
        Assert.AreEqual(HttpMethod.Patch, patch.Method);
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(101)]
    [DataRow(int.MaxValue)]
    public void Cross_device_search_is_bounded(int size) => Assert.ThrowsExactly<FederationAccessException>(() =>
        ManagedLibraryOperations.Build(Search(size)));

    [TestMethod]
    public void Owner_epoch_and_safe_search_flags_cannot_be_overridden()
    {
        var request = Search();
        var operation = ManagedLibraryOperations.Build(request);
        Assert.AreEqual(HttpMethod.Post, operation.Method);
        StringAssert.Contains(operation.Path, "saveSearch=false");
        Assert.IsFalse(ManagedLibraryOperations.MetadataItems.HasFlag(
            Bakabase.InsideWorld.Models.Constants.AdditionalItems.ResourceAdditionalItem.PlayableItem));
        Assert.IsTrue(ManagedLibraryOperations.MetadataItems.HasFlag(
            Bakabase.InsideWorld.Models.Constants.AdditionalItems.ResourceAdditionalItem.Cover));
        StringAssert.Contains(operation.Path, "managedLibraryEpoch=library-one");
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(request with
            {Query = new() {{"managedLibraryEpoch", ["new-epoch"]}}}));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(request with
            {LibraryEpoch = "epoch&saveSearch=true"}));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new(Epoch,
            "resourceGet", Ids: Enumerable.Range(1, 101).ToArray())));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new(Epoch,
            "resourcePropertyPut", Id: -1, Body: new JObject())));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(request with
            {Body = JObject.Parse("{\"pageSize\":24,\"PageSize\":1000000}")}));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(request with
            {Body = JObject.Parse("{\"PageSize\":1000000}")}));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new(Epoch,
            "resourceLibrariesPut", Body: JObject.Parse("{\"ids\":[1],\"Ids\":[2],\"mediaLibraryIds\":[]}"))));
    }

    [TestMethod]
    public void Scope_queries_and_empty_library_assignment_remain_supported()
    {
        var empty = ManagedLibraryOperations.Build(new(Epoch, "resourceLibrariesPut",
            Body: JObject.FromObject(new {ids = new[] {3}, mediaLibraryIds = Array.Empty<int>()})));
        Assert.AreEqual("{\"ids\":[3],\"mediaLibraryIds\":[]}", empty.Json);
        var scope = ManagedLibraryOperations.Build(new(Epoch, "resourceScopePreferencesDelete", Id: 3,
            Query: new() {{"propertyPool", ["4"]}, {"propertyId", ["11"]}}));
        StringAssert.Contains(scope.Path, "/resource/3/property-value-scope-preference?propertyPool=4&propertyId=11");
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new(Epoch, "properties",
            Query: new() {{"pool", ["7/../../gui/url"]}})));
    }

    [TestMethod]
    public async Task Same_numeric_ids_on_two_owners_never_share_a_write_target()
    {
        var (api, wire) = Wire();
        var service = new ManagedLibraryService(api, new Identity());
        var write = new ManagedLibraryRequest(Epoch, "resourcePropertyPut", 123,
            Body: JObject.FromObject(new {propertyId = 7, value = "one", isCustomProperty = true}));
        await Task.WhenAll(service.ExecuteAsync("source-a", write, default),
            service.ExecuteAsync("source-b", write with {Body = JObject.FromObject(new {value = "two"})}, default));
        var writes = wire.Calls.Where(c => c.Method == HttpMethod.Put).ToArray();
        Assert.AreEqual(2, writes.Length);
        Assert.AreEqual("one", JObject.Parse(writes.Single(c => c.ServerId == "source-a").Json!)["value"]);
        Assert.AreEqual("two", JObject.Parse(writes.Single(c => c.ServerId == "source-b").Json!)["value"]);
        Assert.IsFalse(wire.Calls.Any(c => c.ServerId == "this-device"));
    }

    [DataTestMethod]
    [DataRow("epoch", "LibraryEpochChanged")]
    [DataRow("identity", "ManagedSourceChanged")]
    [DataRow("version", "ManagementUpdateRequired")]
    [DataRow("old", "ManagementUpdateRequired")]
    [DataRow("offline", "ManagedSourceUnavailable")]
    public async Task Changed_or_unverified_sources_receive_no_write(string failure, string errorCode)
    {
        var (api, wire) = Wire();
        wire.Failure = failure;
        var service = new ManagedLibraryService(api, new Identity());
        var error = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => service.ExecuteAsync("source-a",
            new(Epoch, "libraryDelete", 123), default));
        Assert.AreEqual(errorCode, error.ErrorCode);
        Assert.IsTrue(wire.Calls.All(c => c.Method == HttpMethod.Get));
    }

    [TestMethod]
    public async Task Unavailable_owners_stay_visible_without_fake_empty_results()
    {
        var (api, wire) = Wire();
        wire.Failure = "offline";
        var sources = await new ManagedLibraryService(api, new Identity()).SourcesAsync(default);
        Assert.AreEqual(3, sources.Sources.Count);
        Assert.IsTrue(sources.Sources.Single(s => s.IsLocal).Available);
        Assert.IsTrue(sources.Sources.Where(s => !s.IsLocal).All(s => !s.Available &&
            s.ErrorCode == "ManagedSourceUnavailable" && s.LibraryEpoch == null));
    }

    [TestMethod]
    public async Task Owner_business_error_is_returned_and_writes_are_never_retried()
    {
        var (api, wire) = Wire();
        wire.WriteResponse = new ManagedServerResponse(409, "application/json", "{\"code\":409,\"message\":\"changed\"}");
        var result = await new ManagedLibraryService(api, new Identity()).ExecuteAsync("source-a", new(Epoch,
            "customPropertyPut", 3, Body: new JObject()), default);
        Assert.AreEqual(409, result.StatusCode);
        Assert.AreEqual(1, wire.Calls.Count(c => c.Method == HttpMethod.Put));
    }

    [TestMethod]
    public async Task Real_controller_binds_json_and_preserves_owner_response()
    {
        var (api, wire) = Wire();
        await using var host = await Host(api);
        using var response = await host.SendAsync(HttpMethod.Post, "/federation/local/management/source-a/request",
            "same-origin", host.Origin, """{"libraryEpoch":"library-one","operation":"resourceSearch","body":{"page":1,"pageSize":24,"keyword":"abc"}}""");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(1, wire.Calls.Count(c => c.Method == HttpMethod.Post));
        Assert.AreEqual("abc", JObject.Parse(wire.Calls.Last().Json!)["keyword"]);
    }

    [TestMethod]
    public async Task Headless_own_origin_can_manage_but_cross_site_cannot()
    {
        var (api, wire) = Wire();
        await using var host = await Host(api);
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var own = host.Request(HttpMethod.Get, "/federation/local/management/sources", "same-origin", host.Origin);
        own.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.1.5");
        using var allowed = await host.SendAsync(own);
        Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode, await allowed.Content.ReadAsStringAsync());
        var before = wire.Calls.Count;
        using var denied = await host.SendAsync(HttpMethod.Post, "/federation/local/management/source-a/request",
            "cross-site", "https://other.invalid", """{"libraryEpoch":"library-one","operation":"libraryDelete","id":3}""");
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.AreEqual(before, wire.Calls.Count);
    }

    [TestMethod]
    public async Task Source_epoch_filter_blocks_reused_ids_before_controller_execution()
    {
        var (api, _) = Wire();
        var identity = new Identity();
        await using var host = await ServiceGateHost.StartAsync([typeof(ManagedEpochProbeController)], services =>
        {
            services.AddSingleton<INodeIdentityProvider>(identity);
            services.Configure<MvcOptions>(o => o.Filters.Add<ManagedLibraryEpochFilter>());
        });
        ManagedEpochProbeController.Writes = 0;
        using var ordinary = await host.SendAsync(HttpMethod.Put, "/test-managed-library/123");
        Assert.AreEqual(HttpStatusCode.OK, ordinary.StatusCode);
        identity.Epoch = "new-library";
        using var stale = await host.SendAsync(HttpMethod.Put, "/test-managed-library/123?managedLibraryEpoch=library-one");
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.AreEqual(1, ManagedEpochProbeController.Writes);
    }

    [TestMethod]
    public async Task Browser_media_uses_the_owner_resource_endpoint_and_keeps_ranges()
    {
        var (api, wire) = Wire();
        wire.WriteResponse = new(200, "application/json", """{"code":0,"data":[{"origin":2,"key":"/media/movie.mp4"}]}""");
        await using var host = await Host(api);
        var path = "/federation/local/management/source-a/media?libraryEpoch=library-one&resourceId=123&origin=2&key=";
        using var wrong = await host.SendAsync(HttpMethod.Get, path + Uri.EscapeDataString("/other/program.exe"));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, wrong.StatusCode, await wrong.Content.ReadAsStringAsync());
        Assert.AreEqual(0, wire.MediaRequests.Count);
        using var request = host.Request(HttpMethod.Get, path + Uri.EscapeDataString("/media/movie.mp4"));
        request.Headers.Range = new(2, 4);
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.PartialContent, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("234", await response.Content.ReadAsStringAsync());
        Assert.AreEqual("bytes 2-4/10", response.Content.Headers.ContentRange!.ToString());
        CollectionAssert.Contains(response.Headers.GetValues("Content-Security-Policy").ToArray(), "sandbox; default-src 'none'");
        StringAssert.Contains(wire.MediaRequests.Single(), "managedLibraryEpoch=library-one");
        StringAssert.Contains(wire.MediaRequests.Single(), "/management/library/resources/123/media?");
        StringAssert.Contains(wire.MediaRequests.Single(), "bytes=2-4");
    }

    [TestMethod]
    public async Task A_headless_manager_cannot_launch_a_native_program_even_for_loopback()
    {
        var (api, wire) = Wire();
        await using var host = await Host(api);
        using var response = await host.SendAsync(HttpMethod.Post, "/federation/local/management/source-a/play",
            "same-origin", host.Origin, """{"libraryEpoch":"library-one","resourceId":123,"origin":2,"key":"/media/movie.mp4"}""");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(0, wire.Calls.Count);
    }

    [TestMethod]
    public void Normal_resource_actions_keep_owner_routes_bounded_and_do_not_expand_playable_files()
    {
        var get = ManagedLibraryOperations.Build(new(Epoch, "resourceGet", Ids: [5, 8],
            Query: new() {{"additionalItems", ["117600"]}}));
        var flags = int.Parse(System.Web.HttpUtility.ParseQueryString(new Uri("http://owner" + get.Path).Query)["additionalItems"]!);
        Assert.AreEqual(0, flags & (int) Bakabase.InsideWorld.Models.Constants.AdditionalItems.ResourceAdditionalItem.PlayableItem);
        Assert.AreNotEqual(0, flags & (int) Bakabase.InsideWorld.Models.Constants.AdditionalItems.ResourceAdditionalItem.HasChildren);
        var bulk = JObject.FromObject(new {resourceIds = new[] {5, 8}, propertyId = 11, value = "x"});
        var put = ManagedLibraryOperations.Build(new(Epoch, "bulkPutResourcePropertyValue", Body: bulk));
        Assert.AreEqual(HttpMethod.Put, put.Method);
        StringAssert.StartsWith(put.Path, "/resource/bulk/property-value?");
        Assert.IsTrue(JToken.DeepEquals(bulk, JToken.Parse(put.Json!)));
        var remove = ManagedLibraryOperations.Build(new(Epoch, "removeCollectionMembers", 4,
            Body: JObject.FromObject(new {resourceIds = new[] {5, 8}})));
        Assert.AreEqual(HttpMethod.Delete, remove.Method);
        Assert.IsNotNull(remove.Json);
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new(Epoch,
            "bulkPutResourcePropertyValue", Body: JObject.FromObject(new {resourceIds = Enumerable.Range(1, 101)}))));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new(Epoch,
            "deleteEnhancementsByResources", Body: new JArray(0))));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new(Epoch,
            "enhanceResourceByEnhancer", 1, Query: new() {{"enhancerId", ["1/../../gui/url"]}})));
    }

    [TestMethod]
    public void Source_specific_filter_and_search_tab_values_are_encoded_and_owner_bound()
    {
        var filter = ManagedLibraryOperations.Build(new(Epoch, "getFilterValueProperty", Query: new()
            {{"propertyPool", ["4"]}, {"propertyId", ["12"]}, {"operation", ["1"]}}));
        StringAssert.Contains(filter.Path, "propertyPool=4&propertyId=12&operation=1");
        var saved = ManagedLibraryOperations.Build(new(Epoch, "putSavedSearchName", Body: new JValue("My search"),
            Query: new() {{"id", ["abc&managedLibraryEpoch=new"]}}));
        StringAssert.Contains(saved.Path, "id=abc%26managedLibraryEpoch%3Dnew");
        StringAssert.EndsWith(saved.Path, "managedLibraryEpoch=library-one");
        Assert.AreEqual("\"My search\"", saved.Json);
        var refresh = ManagedLibraryOperations.Build(new(Epoch, "refreshResourceCache", 8));
        Assert.IsNull(refresh.Json);
        var children = ManagedLibraryOperations.Build(new(Epoch, "getChildrenIwFsInfo", 8,
            Query: new() {{"root", ["/media/album"]}, {"showHiddenFiles", ["false"]}}));
        StringAssert.StartsWith(children.Path, "/management/library/resources/8/children?");
    }

    [TestMethod]
    public async Task Cover_delivery_uses_only_a_current_owner_resource_and_cover_index()
    {
        var (api, wire) = Wire();
        await using var host = await Host(api);
        var path = "/federation/local/management/source-a/cover?libraryEpoch=library-one&resourceId=123&index=2&w=600&h=600";
        using var response = await host.SendAsync(HttpMethod.Get, path);
        Assert.AreEqual(HttpStatusCode.PartialContent, response.StatusCode);
        StringAssert.Contains(wire.MediaRequests.Single(), "/management/library/resources/123/cover?index=2&w=600&h=600&managedLibraryEpoch=library-one");
        Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
        wire.MediaRequests.Clear();
        wire.Failure = "epoch";
        using var stale = await host.SendAsync(HttpMethod.Get, path);
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.AreEqual(0, wire.MediaRequests.Count);
    }

    [DataTestMethod]
    [DataRow(0, 0, 600)]
    [DataRow(1, -1, 600)]
    [DataRow(1, 100, 600)]
    [DataRow(1, 0, 0)]
    [DataRow(1, 0, 2049)]
    public async Task Invalid_covers_do_not_make_any_owner_request(int resourceId, int index, int width)
    {
        var (api, wire) = Wire();
        await using var host = await Host(api);
        using var response = await host.SendAsync(HttpMethod.Get,
            $"/federation/local/management/source-a/cover?libraryEpoch=library-one&resourceId={resourceId}&index={index}&w={width}");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, wire.Calls.Count);
        Assert.AreEqual(0, wire.MediaRequests.Count);
    }

    [TestMethod]
    public async Task A_headless_manager_cannot_open_a_native_folder_and_upload_route_cannot_forward_other_writes()
    {
        var (api, wire) = Wire();
        await using var host = await Host(api);
        using var folder = await host.SendAsync(HttpMethod.Post, "/federation/local/management/source-a/directory",
            "same-origin", host.Origin, """{"libraryEpoch":"library-one","resourceId":123}""");
        Assert.AreEqual(HttpStatusCode.Forbidden, folder.StatusCode);
        foreach (var route in new[] {"batch-play", "batch-play/candidates"})
        {
            using var batch = await host.SendAsync(HttpMethod.Post, "/federation/local/management/source-a/" + route,
                "same-origin", host.Origin, """{"libraryEpoch":"library-one","resourceIds":[123],"playerKey":"known|Vlc"}""");
            Assert.AreEqual(HttpStatusCode.Forbidden, batch.StatusCode);
        }
        using var write = await host.SendAsync(HttpMethod.Post, "/federation/local/management/source-a/upload",
            "same-origin", host.Origin, """{"libraryEpoch":"library-one","operation":"libraryDelete","id":123}""");
        Assert.AreEqual(HttpStatusCode.BadRequest, write.StatusCode);
        Assert.AreEqual(0, wire.Calls.Count);
    }

    [DataTestMethod]
    [DataRow("batch-play")]
    [DataRow("batch-play/candidates")]
    public async Task Native_batch_bridge_preserves_order_and_intentional_duplicate_resources(string route)
    {
        var (api, wire) = Wire();
        await using var host = await Host(api, ServerKind.Desktop);
        using var response = await host.SendAsync(HttpMethod.Post, "/federation/local/management/source-a/" + route,
            "same-origin", host.Origin, """{"libraryEpoch":"library-one","resourceIds":[43,42,43],"playerKey":"known|Vlc"}""");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        CollectionAssert.AreEqual(new[] {43, 42, 43}, wire.BatchSelections.Single());
    }

    private static Task<ServiceGateHost> Host(IManagedServerService api, ServerKind kind = ServerKind.Headless) => ServiceGateHost.StartAsync(
        [typeof(ManagedLibraryController), typeof(ManagedLibraryInfoController), typeof(ManagedLibraryPlaybackController), typeof(ManagedLibraryCoverController)], services =>
        {
            services.AddSingleton(api);
            services.AddSingleton<INodeIdentityProvider>(new Identity());
            services.AddSingleton<IServerSelfDescription>(new ServerSelfDescription(() => kind));
            services.AddScoped<ManagedLibraryService>();
        });

    private static (IManagedServerService, ManagementWire) Wire()
    {
        var api = DispatchProxy.Create<IManagedServerService, ManagementWire>();
        return (api, (ManagementWire) (object) api);
    }
    public class Identity : INodeIdentityProvider
    {
        public string Epoch = ManagedLibraryTests.Epoch;
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NodeIdentity("this-device", Epoch, "This device"));
    }
    public sealed record Call(string ServerId, HttpMethod Method, string Path, string? Json);
    public class ManagementWire : DispatchProxy
    {
        public ConcurrentQueue<Call> Calls = new();
        public ConcurrentQueue<string> MediaRequests = new();
        public ConcurrentQueue<int[]> BatchSelections = new();
        public string? Failure;
        public ManagedServerResponse WriteResponse = new(200, "application/json", "{\"code\":0,\"data\":[]}");
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IManagedServerService.GetAsync))
                return Task.FromResult(new ManagedServersView(true, new[] {"source-a", "source-b"}.Select(id =>
                    FakeManagedServerService.Server with {ServerId = id}).ToArray(), []));
            if (method.Name == nameof(IManagedServerService.BatchPlayManagedResourcesAsync))
            {
                BatchSelections.Enqueue((int[]) args![1]!);
                return Task.FromResult(WriteResponse);
            }
            if (method.Name == nameof(IManagedServerService.SendMediaAsync))
            {
                MediaRequests.Enqueue($"{args![0]} {args[2]} {args[3]}");
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) {Content = new StringContent("234")};
                response.Content.Headers.ContentRange = new(2, 4, 10);
                return ((Func<HttpResponseMessage, CancellationToken, Task>) args[4]!)(response, (CancellationToken) args[5]!);
            }
            if (method.Name != nameof(IManagedServerService.SendAsync)) throw new NotSupportedException();
            var call = new Call((string) args![0]!, (HttpMethod) args[1]!, (string) args[2]!, (string?) args[3]);
            Calls.Enqueue(call);
            if (Failure == "offline") throw new HttpRequestException();
            if (call.Path != "/management/library/info") return Task.FromResult(WriteResponse);
            return Task.FromResult(Failure == "old" ? new ManagedServerResponse(404, "application/json", "{}") :
                new ManagedServerResponse(200, "application/json", JsonConvert.SerializeObject(new ManagedLibraryInfo(
                    Failure == "identity" ? "other" : call.ServerId, "Source", Failure == "epoch" ? "other" : Epoch,
                    Failure == "version" ? 2 : 1))));
        }
    }
}

[ApiController]
[Route("test-managed-library")]
public sealed class ManagedEpochProbeController : ControllerBase
{
    public static int Writes;
    [HttpPut("{id:int}")]
    public IActionResult Write(int id) { Interlocked.Increment(ref Writes); return Ok(); }
}
