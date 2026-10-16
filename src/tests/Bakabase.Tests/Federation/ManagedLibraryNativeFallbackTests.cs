using System.Net;
using System.Reflection;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class ManagedLibraryNativeFallbackTests
{
    [DataTestMethod]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, true)]
    public async Task Only_an_explicit_owner_approved_file_reaches_the_native_adapter(bool explicitChoice,
        bool ownerAllows, bool shouldLaunch)
    {
        var api = DispatchProxy.Create<IManagedServerService, Wire>();
        var wire = (Wire)(object)api;
        wire.Allowed = ownerAllows;
        var controller = new ManagedLibraryPlaybackController(
            new ManagedLibraryService(api, new Identity()), api)
        {
            ControllerContext = new ControllerContext {HttpContext = new DefaultHttpContext()}
        };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        controller.Request.Host = new HostString("127.0.0.1", 34567);
        var request = new ManagedLibraryPlayRequest("library-one", 42, DataOrigin.FileSystem, "/album/page.png", explicitChoice);
        var self = new ServerSelfDescription(() => ServerKind.Desktop);
        if (shouldLaunch)
        {
            var result = await controller.Play("source-a", request, self, default);
            Assert.IsInstanceOfType<ContentResult>(result);
            Assert.AreEqual(1, wire.Launches);
            Assert.IsTrue(wire.ExplicitChoice);
        }
        else
        {
            var error = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
                controller.Play("source-a", request, self, default));
            Assert.AreEqual("PlayableItemChanged", error.ErrorCode);
            Assert.AreEqual(0, wire.Launches);
        }
        Assert.AreEqual(explicitChoice ? 1 : 0, wire.Validations.Count);
        if (explicitChoice)
        {
            StringAssert.Contains(wire.Validations[0], "/resources/42/native-fallback?");
            StringAssert.Contains(wire.Validations[0], "managedLibraryEpoch=library-one");
            StringAssert.Contains(wire.Validations[0], "key=%2Falbum%2Fpage.png");
        }
    }

    public class Identity : INodeIdentityProvider
    {
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NodeIdentity("this-device", "library-one", "This device"));
    }

    public class Wire : DispatchProxy
    {
        public bool Allowed;
        public bool ExplicitChoice;
        public int Launches;
        public List<string> Validations = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IManagedServerService.PlayManagedResourceAsync))
            {
                Launches++;
                ExplicitChoice = (bool)args![5]!;
                return Task.FromResult(new ManagedServerResponse(200, "application/json", "{\"code\":0}"));
            }
            if (method.Name != nameof(IManagedServerService.SendAsync)) throw new NotSupportedException();
            var path = (string)args![2]!;
            string body;
            if (path == "/management/library/info") body = JsonConvert.SerializeObject(new ManagedLibraryInfo("source-a", "Owner", "library-one"));
            else if (path.Contains("native-fallback"))
            {
                Validations.Add(path);
                body = JsonConvert.SerializeObject(new {code = 0, data = Allowed});
            }
            else body = "{\"code\":0,\"data\":[]}";
            return Task.FromResult(new ManagedServerResponse(200, "application/json", body));
        }
    }
}
