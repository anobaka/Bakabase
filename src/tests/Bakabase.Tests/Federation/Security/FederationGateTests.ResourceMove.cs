using Bakabase.Abstractions.Models.Domain.Constants;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation.Security;

public sealed partial class FederationGateTests
{
    [TestMethod]
    [DataRow("/resource-move", "POST")]
    [DataRow("/resource-move/context", "GET")]
    [DataRow("/resource-move/preview", "POST")]
    [DataRow("/resource-move/panel-options", "GET")]
    [DataRow("/resource-move/panel-options", "PUT")]
    [DataRow("/resource-move/batches", "GET")]
    [DataRow("/resource-move/batches/a/cancel", "POST")]
    [DataRow("/resource-move/batches/a/retry", "POST")]
    [DataRow("/resource-move/records/1/resolve", "POST")]
    [DataRow("/resource-move/records/1/retry", "POST")]
    [DataRow("/resource-move/records/1", "DELETE")]
    public async Task SharingCredentialsCannotReachLocalMoveManagement(string path, string method)
    {
        using var fixture = await GateFixture.CreateAsync();
        foreach (var mode in new[] { RemoteAccessMode.Enabled, RemoteAccessMode.Unrestricted })
        foreach (var ip in new[] { "127.0.0.1", "192.168.20.8" })
        {
            fixture.Remote.Mode = mode;
            var context = Context(path, method, ip);
            fixture.Sign(context);
            await fixture.RunAsync(context);
            Assert.AreEqual(403, context.Response.StatusCode, $"{path} {mode} {ip}");
            Assert.AreEqual("NodeRouteForbidden", Error(context));
            Assert.IsFalse(fixture.ReachedEndpoint);
        }
    }
}
