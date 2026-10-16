using System.Net;
using System.Text;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Controllers;
using Bakabase.Tests.Federation.Security;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests.Federation;

[TestClass]
[DoNotParallelize] // BAKABASE_NODE_NAME is a process-wide startup setting.
public sealed class ManagedDeviceNameTests
{
    private string? _previousName;

    [TestInitialize]
    public void ClearEnvironmentName()
    {
        _previousName = Environment.GetEnvironmentVariable(FederationStateStore.DisplayNameVariable);
        Environment.SetEnvironmentVariable(FederationStateStore.DisplayNameVariable, null);
    }

    [TestCleanup]
    public void RestoreEnvironmentName() =>
        Environment.SetEnvironmentVariable(FederationStateStore.DisplayNameVariable, _previousName);

    private static Task<ServiceGateHost> Host() =>
        ServiceGateHost.StartAsync([typeof(ManagedDeviceNameController), typeof(HeadlessFederationProbeController)]);

    [TestMethod]
    public void Rename_is_an_explicit_epoch_bound_operation_and_cannot_supply_other_state()
    {
        var read = ManagedLibraryOperations.Build(new("epoch-one", "deviceNameGet"));
        Assert.AreEqual(HttpMethod.Get, read.Method);
        Assert.AreEqual("/management/device-name?managedLibraryEpoch=epoch-one", read.Path);
        var write = ManagedLibraryOperations.Build(new("epoch-one", "deviceNameSet",
            Body: JObject.FromObject(new {name = "NAS", expectedName = "Old"})));
        Assert.AreEqual(HttpMethod.Put, write.Method);
        var body = JObject.Parse(write.Json!);
        Assert.AreEqual("epoch-one", body["libraryEpoch"]);
        Assert.AreEqual("Old", body["expectedName"]);
        foreach (var invalid in new[] {
                     "{\"name\":\"NAS\"}",
                     "{\"name\":\"NAS\",\"expectedName\":\"Old\",\"libraryEpoch\":\"other\"}",
                     "{\"name\":\"NAS\",\"expectedName\":\"Old\",\"nodeId\":\"other\"}"})
            Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryOperations.Build(new("epoch-one",
                "deviceNameSet", Body: JObject.Parse(invalid))));
    }

    [TestMethod]
    public async Task Concurrent_rename_is_compare_and_swap_and_preserves_identity_and_other_state()
    {
        await using var host = await Host();
        var store = host.Services.GetRequiredService<FederationStateStore>();
        await store.SetDisplayNameAsync("Original");
        var before = await store.GetIdentityAsync();
        var filename = Path.Combine(host.Root, "federation", FederationStateStore.FileName);
        var originalState = JObject.Parse(await File.ReadAllTextAsync(filename));
        originalState.Remove("displayName");
        async Task<string> Rename(string name)
        {
            try { return (await store.ChangeDisplayNameAsync(name, "Original", before.LibraryEpoch)).Name; }
            catch (FederationAccessException error) { return error.ErrorCode; }
        }
        var answers = await Task.WhenAll(Rename("First"), Rename("Second"));
        Assert.AreEqual(1, answers.Count(a => a == "DeviceNameChanged"));
        var after = await store.GetIdentityAsync();
        Assert.AreEqual(before.NodeId, after.NodeId);
        Assert.AreEqual(before.LibraryEpoch, after.LibraryEpoch);
        var saved = JObject.Parse(await File.ReadAllTextAsync(filename));
        saved.Remove("displayName");
        Assert.IsTrue(JToken.DeepEquals(originalState, saved));
        var epochError = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            store.ChangeDisplayNameAsync("Wrong library", after.Name, "replaced-epoch"));
        Assert.AreEqual("LibraryEpochChanged", epochError.ErrorCode);
        Assert.AreEqual(after, await store.GetIdentityAsync());
        await store.ChangeDisplayNameAsync(null, after.Name, after.LibraryEpoch);
        Assert.AreEqual(Environment.MachineName, (await store.GetIdentityAsync()).Name);
    }

    [TestMethod]
    public async Task Environment_name_is_reported_and_refused_instead_of_saving_an_ineffective_name()
    {
        await using var host = await Host();
        var store = host.Services.GetRequiredService<FederationStateStore>();
        await store.SetDisplayNameAsync("Stored");
        var before = await store.GetIdentityAsync();
        Environment.SetEnvironmentVariable(FederationStateStore.DisplayNameVariable, "Compose name");
        using var read = await host.SendAsync(HttpMethod.Get, "/management/device-name");
        var view = JObject.Parse(await read.Content.ReadAsStringAsync());
        Assert.AreEqual("Compose name", view["name"]);
        Assert.AreEqual(true, view["environmentControlled"]);
        using var write = await host.SendAsync(HttpMethod.Put, "/management/device-name", "same-origin", host.Origin,
            JsonConvert.SerializeObject(new {name = "Ignored", expectedName = "Compose name", libraryEpoch = before.LibraryEpoch}));
        Assert.AreEqual(HttpStatusCode.Conflict, write.StatusCode);
        StringAssert.Contains(await write.Content.ReadAsStringAsync(), "DeviceNameEnvironmentControlled");
        await Assert.ThrowsExactlyAsync<FederationAccessException>(() => store.SetDisplayNameAsync("Also ignored"));
        Environment.SetEnvironmentVariable(FederationStateStore.DisplayNameVariable, null);
        Assert.AreEqual("Stored", (await store.GetIdentityAsync()).Name);
    }

    [TestMethod]
    public async Task Only_management_authority_can_rename_a_desktop_and_local_federation_remains_local()
    {
        await using var host = await Host();
        host.Remote.Mode = RemoteAccessMode.Enabled;
        host.Remote.RequirePairing = false;
        var store = host.Services.GetRequiredService<FederationStateStore>();
        var before = await store.GetIdentityAsync();
        var devices = host.Services.GetRequiredService<IRemoteDeviceService>();
        var code = await devices.IssuePairingCodeAsync();
        var pair = await devices.PairWithCodeAsync(code.Code, "Manager", RemoteDevicePlatform.Windows);
        var credentials = pair.Credentials!;
        var json = JsonConvert.SerializeObject(new {name = "Living room", expectedName = before.Name, libraryEpoch = before.LibraryEpoch});
        HttpRequestMessage Request(string path = "/management/device-name", string? authorization = null)
        {
            var request = host.Request(HttpMethod.Put, path, "same-origin", host.Origin, json);
            request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.20.8");
            if (authorization != null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
            return request;
        }
        using (var unsigned = Request())
        using (var refused = await host.SendAsync(unsigned))
            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
        using (var node = Request(authorization: "Bakabase-Node invalid"))
        using (var refused = await host.SendAsync(node))
            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
        string Signature(string path)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = Guid.NewGuid().ToString("N");
            var canonical = RemoteRequestSignature.BuildCanonicalString(credentials.DeviceId, "PUT", path, "", timestamp,
                nonce, RemoteRequestSignature.HashBody(Encoding.UTF8.GetBytes(json)));
            return RemoteRequestSignature.BuildHeader(credentials.DeviceId, timestamp, nonce,
                RemoteRequestSignature.Sign(RemoteRequestSignature.FromBase64Url(credentials.Key), canonical));
        }
        using (var signed = Request(authorization: Signature("/management/device-name")))
        using (var renamed = await host.SendAsync(signed))
        {
            Assert.AreEqual(HttpStatusCode.OK, renamed.StatusCode, await renamed.Content.ReadAsStringAsync());
            StringAssert.Contains(await renamed.Content.ReadAsStringAsync(), "Living room");
        }
        using (var stale = Request(authorization: Signature("/management/device-name")))
        using (var conflict = await host.SendAsync(stale))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
            StringAssert.Contains(await conflict.Content.ReadAsStringAsync(), "DeviceNameChanged");
        }
        // No new permission was granted to the desktop-only /federation/local boundary.
        using (var localRoute = host.Request(HttpMethod.Get, "/federation/local/peers", "same-origin", host.Origin))
        {
            localRoute.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.20.8");
            using var denied = await host.SendAsync(localRoute);
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        Assert.AreEqual(before.NodeId, (await store.GetIdentityAsync()).NodeId);
        Assert.AreEqual(before.LibraryEpoch, (await store.GetIdentityAsync()).LibraryEpoch);
    }
}
