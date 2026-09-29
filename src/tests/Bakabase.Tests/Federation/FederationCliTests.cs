using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Service.Components.Federation;
using Bakabase.Tests.RemoteAccess;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// The headless server's command line, which calls the running instance's loopback API: the
/// one way to manage a server whose own UI is only ever reached from another device.
/// </summary>
[TestClass]
public class FederationCliTests
{
    [TestMethod]
    public async Task New_identity_asks_the_running_instance_for_a_new_identity_as_a_copy()
    {
        // A copied Docker volume answers with the original's identity, and the devices page's
        // "Create a new device identity" cannot be reached from another device.
        var port = LoopbackPortAllocator.Allocate(47600);
        string? method = null, path = null, body = null;

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions {Args = []});
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port));
        await using var app = builder.Build();
        app.Urls.Clear();
        app.Run(async context =>
        {
            method = context.Request.Method;
            path = context.Request.Path.Value;
            body = await new StreamReader(context.Request.Body).ReadToEndAsync();
            await context.Response.WriteAsJsonAsync(new {nodeId = "new-node"});
        });
        await app.StartAsync();

        var exit = await FederationCli.RunAsync(["federation", "new-identity", "--port", port.ToString()]);

        Assert.AreEqual(0, exit);
        Assert.AreEqual("POST", method);
        Assert.AreEqual("/federation/local/peers/identity/reset", path);

        // A copy, not the recovery of an unreadable state: the install's identity goes too.
        using var sent = JsonDocument.Parse(body!);
        Assert.IsTrue(sent.RootElement.GetProperty("asNewNode").GetBoolean());
        Assert.IsTrue(sent.RootElement.GetProperty("replaceInstallIdentity").GetBoolean());
    }
}
