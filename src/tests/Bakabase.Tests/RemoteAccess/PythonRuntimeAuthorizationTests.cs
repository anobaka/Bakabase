using System.Reflection;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public sealed class PythonRuntimeAuthorizationTests
{
    [DataTestMethod]
    [DataRow(nameof(PythonRuntimeController.GetConfiguration))]
    [DataRow(nameof(PythonRuntimeController.GetCandidates))]
    [DataRow(nameof(PythonRuntimeController.Validate))]
    [DataRow(nameof(PythonRuntimeController.Apply))]
    public void PythonConfigurationRequiresTheSelectedServersAdministrationBoundary(string action)
    {
        var scenarios = new (RemoteAccessContext? Remote, bool Allowed)[]
        {
            (null, false),
            (new() { IsLoopback = false, Mode = RemoteAccessMode.Enabled }, false),
            (new() { IsLoopback = false, Mode = RemoteAccessMode.Enabled, Device = new RemoteDevice { Id = "paired-administrator" } }, true),
            (new() { IsLoopback = false, Mode = RemoteAccessMode.Unrestricted }, true),
            (new() { IsLoopback = true, Mode = RemoteAccessMode.Disabled }, true)
        };
        foreach (var (remote, allowed) in scenarios)
        {
            var http = new DefaultHttpContext();
            if (remote != null) http.SetRemoteAccessContext(remote);
            var descriptor = new ControllerActionDescriptor
            {
                MethodInfo = typeof(PythonRuntimeController).GetMethod(action)!,
                ControllerTypeInfo = typeof(PythonRuntimeController).GetTypeInfo()
            };
            var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), []);
            new RemoteAccessAuthorizationFilter(new ServerSelfDescription(() => ServerKind.Headless))
                .OnAuthorization(context);
            Assert.AreEqual(allowed, context.Result == null, action);
            if (!allowed)
            {
                Assert.IsInstanceOfType<ObjectResult>(context.Result);
                Assert.AreEqual(403, ((ObjectResult)context.Result!).StatusCode);
                Assert.AreEqual(nameof(RemoteAccessDenialReason.HostOnly),
                    context.HttpContext.Response.Headers["X-Bakabase-Remote-Access"].ToString());
            }
        }
    }

    [TestMethod]
    public void ControllerExplicitlyProtectsInterpreterExecutionAndServerPathDiscovery()
    {
        var marker = typeof(PythonRuntimeController).GetCustomAttribute<RemoteAccessibleAttribute>();
        Assert.IsNotNull(marker);
        Assert.IsFalse(marker.Allowed);
        Assert.IsNull(typeof(PythonRuntimeController).GetCustomAttribute<RunsOnUserMachineAttribute>(),
            "The selected server owns its interpreter, including when administered remotely.");
    }
}
