using System.Collections.Generic;
using System.Reflection;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// The gate's ordering: the all-in-one keeps its own desktop actions, and a headless
/// server cannot launch them even where everything else is allowed.
/// </summary>
[TestClass]
public class RemoteAccessAuthorizationFilterTests
{
    private sealed class Actions
    {
        [RunsOnUserMachine(Reason = "A player starts on the machine you are sitting at.")]
        public void LaunchesAPlayer()
        {
        }

        [RemoteAccessible]
        public void PlainData()
        {
        }

        public void Unmarked()
        {
        }

        [RunsOnUserMachine]
        [RemoteAccessible]
        public void BothMarkers()
        {
        }
    }

    [RunsOnUserMachine(Reason = "controller-level")]
    private sealed class UserMachineController
    {
        public void Inherited()
        {
        }
    }

    private static AuthorizationFilterContext Build(string actionName, RemoteAccessContext? remote,
        System.Type? controller = null)
    {
        var http = new DefaultHttpContext();
        if (remote != null)
        {
            http.SetRemoteAccessContext(remote);
        }

        var type = controller ?? typeof(Actions);
        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = type.GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance)!,
            ControllerTypeInfo = type.GetTypeInfo()
        };

        return new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), descriptor),
            new List<IFilterMetadata>());
    }

    private static RemoteAccessContext Loopback => new() {IsLoopback = true, Mode = RemoteAccessMode.Disabled};

    private static RemoteAccessContext Remote(RemoteAccessMode mode) =>
        new() {IsLoopback = false, Mode = mode};

    private static string? DenialReason(AuthorizationFilterContext context) =>
        context.HttpContext.Response.Headers.TryGetValue("X-Bakabase-Remote-Access", out var v)
            ? v.ToString()
            : null;

    [TestMethod]
    public void Loopback_passes_even_for_a_user_machine_action()
    {
        // Compositions predating the host descriptor retain the all-in-one's local
        // behavior. An explicit headless descriptor is tested separately below.
        var context = Build(nameof(Actions.LaunchesAPlayer), Loopback);

        new RemoteAccessAuthorizationFilter().OnAuthorization(context);

        Assert.IsNull(context.Result);
        Assert.IsNull(DenialReason(context));
    }

    [DataTestMethod]
    [DataRow(RemoteAccessMode.Enabled, false, false)]
    [DataRow(RemoteAccessMode.Enabled, true, true)]
    [DataRow(RemoteAccessMode.Unrestricted, false, true)]
    public void AdvertisedAddressChangesRequireTheSameOperatorAsSettings(RemoteAccessMode mode, bool paired, bool allowed)
    {
        foreach (var action in new[] {nameof(RemoteAccessController.SetAdvertisedAddress), nameof(RemoteAccessController.ObserveAddress)})
        {
            var context = Build(action, new RemoteAccessContext
            {
                IsLoopback = false, Mode = mode,
                Device = paired ? new RemoteDevice {Id = "paired"} : null
            }, typeof(RemoteAccessController));
            new RemoteAccessAuthorizationFilter().OnAuthorization(context);
            if (allowed) Assert.IsNull(context.Result, action);
            else
            {
                Assert.IsInstanceOfType<ObjectResult>(context.Result);
                Assert.AreEqual(403, ((ObjectResult) context.Result!).StatusCode, action);
            }
        }
    }

    [DataTestMethod]
    [DataRow(RemoteAccessMode.Enabled, false, false, false)]
    [DataRow(RemoteAccessMode.Enabled, false, true, true)]
    [DataRow(RemoteAccessMode.Unrestricted, false, false, true)]
    [DataRow(RemoteAccessMode.Disabled, true, false, true)]
    public void Deployment_paths_keep_the_same_operator_boundary_as_app_info(RemoteAccessMode mode,
        bool loopback, bool paired, bool allowed)
    {
        var remote = new RemoteAccessContext
        {
            IsLoopback = loopback, Mode = mode,
            Device = paired ? new RemoteDevice { Id = "paired-test-device" } : null
        };
        foreach (var (controller, action) in new[]
                 {
                     (typeof(DeploymentPathsController), nameof(DeploymentPathsController.Get)),
                     (typeof(AppController), nameof(AppController.Info))
                 })
        {
            var context = Build(action, remote, controller);
            new RemoteAccessAuthorizationFilter(new ServerSelfDescription(() => ServerKind.Headless))
                .OnAuthorization(context);
            Assert.AreEqual(allowed, context.Result == null, controller.Name);
            if (!allowed) Assert.AreEqual(nameof(RemoteAccessDenialReason.HostOnly), DenialReason(context));
        }
    }

    [DataTestMethod]
    [DataRow(ServerKind.Desktop, true, true)]
    [DataRow(ServerKind.Desktop, false, false)]
    [DataRow(ServerKind.Headless, true, false)]
    [DataRow(ServerKind.Headless, false, false)]
    public void User_machine_actions_need_a_local_desktop(ServerKind kind, bool loopback, bool allowed)
    {
        var context = Build(nameof(Actions.LaunchesAPlayer),
            new RemoteAccessContext {IsLoopback = loopback, Mode = RemoteAccessMode.Unrestricted});

        new RemoteAccessAuthorizationFilter(new ServerSelfDescription(() => kind)).OnAuthorization(context);

        if (allowed)
        {
            Assert.IsNull(context.Result);
            Assert.IsNull(DenialReason(context));
        }
        else
        {
            Assert.IsInstanceOfType<ObjectResult>(context.Result);
            Assert.AreEqual(403, ((ObjectResult) context.Result!).StatusCode);
            Assert.AreEqual(nameof(RemoteAccessDenialReason.RunsOnUserMachine), DenialReason(context));
        }
    }

    [DataTestMethod]
    [DataRow(nameof(Actions.PlainData))]
    [DataRow(nameof(Actions.Unmarked))]
    public void Headless_loopback_still_passes_ordinary_actions(string action)
    {
        var context = Build(action, Loopback);

        new RemoteAccessAuthorizationFilter(new ServerSelfDescription(() => ServerKind.Headless))
            .OnAuthorization(context);

        Assert.IsNull(context.Result);
        Assert.IsNull(DenialReason(context));
    }

    [TestMethod]
    public void Headless_loopback_also_refuses_a_controller_level_user_machine_marker()
    {
        var context = Build(nameof(UserMachineController.Inherited), Loopback, typeof(UserMachineController));

        new RemoteAccessAuthorizationFilter(new ServerSelfDescription(() => ServerKind.Headless))
            .OnAuthorization(context);

        Assert.AreEqual(nameof(RemoteAccessDenialReason.RunsOnUserMachine), DenialReason(context));
    }

    [TestMethod]
    public void Loopback_passes_an_unmarked_action()
    {
        var context = Build(nameof(Actions.Unmarked), Loopback);

        new RemoteAccessAuthorizationFilter().OnAuthorization(context);

        Assert.IsNull(context.Result);
    }

    [TestMethod]
    public void Unrestricted_still_refuses_a_user_machine_action()
    {
        // The container default. Before this ordering, a remote browser here started a
        // player on the server and was told it worked.
        var context = Build(nameof(Actions.LaunchesAPlayer), Remote(RemoteAccessMode.Unrestricted));

        new RemoteAccessAuthorizationFilter().OnAuthorization(context);

        Assert.IsInstanceOfType<ObjectResult>(context.Result);
        Assert.AreEqual(403, ((ObjectResult) context.Result!).StatusCode);
        Assert.AreEqual(nameof(RemoteAccessDenialReason.RunsOnUserMachine), DenialReason(context));
    }

    [TestMethod]
    public void RemoteAccessible_does_not_rescue_a_user_machine_action()
    {
        // The two markers answer different questions; carrying both must not make the
        // action runnable somewhere it means nothing.
        var context = Build(nameof(Actions.BothMarkers), Remote(RemoteAccessMode.Unrestricted));

        new RemoteAccessAuthorizationFilter().OnAuthorization(context);

        Assert.AreEqual(nameof(RemoteAccessDenialReason.RunsOnUserMachine), DenialReason(context));
    }

    [TestMethod]
    public void A_controller_level_marker_covers_its_actions()
    {
        var context = Build(nameof(UserMachineController.Inherited), Remote(RemoteAccessMode.Enabled),
            typeof(UserMachineController));

        new RemoteAccessAuthorizationFilter().OnAuthorization(context);

        Assert.AreEqual(nameof(RemoteAccessDenialReason.RunsOnUserMachine), DenialReason(context));
    }

    [TestMethod]
    public void Unrestricted_passes_an_ordinary_action()
    {
        var context = Build(nameof(Actions.Unmarked), Remote(RemoteAccessMode.Unrestricted));

        new RemoteAccessAuthorizationFilter().OnAuthorization(context);

        Assert.IsNull(context.Result);
    }

    [TestMethod]
    public void Enabled_keeps_default_deny_for_an_unmarked_action()
    {
        var context = Build(nameof(Actions.Unmarked), Remote(RemoteAccessMode.Enabled));

        new RemoteAccessAuthorizationFilter().OnAuthorization(context);

        Assert.AreEqual(nameof(RemoteAccessDenialReason.HostOnly), DenialReason(context));
    }

    [TestMethod]
    public void Enabled_passes_a_remote_accessible_action()
    {
        var context = Build(nameof(Actions.PlainData), Remote(RemoteAccessMode.Enabled));

        new RemoteAccessAuthorizationFilter().OnAuthorization(context);

        Assert.IsNull(context.Result);
    }

    [DataTestMethod]
    [DataRow(ServerKind.Desktop, RemoteAccessMode.Enabled, false, false, false)]
    [DataRow(ServerKind.Desktop, RemoteAccessMode.Enabled, false, true, true)]
    [DataRow(ServerKind.Desktop, RemoteAccessMode.Unrestricted, false, false, true)]
    [DataRow(ServerKind.Desktop, RemoteAccessMode.Disabled, true, false, true)]
    [DataRow(ServerKind.Headless, RemoteAccessMode.Enabled, false, false, false)]
    [DataRow(ServerKind.Headless, RemoteAccessMode.Enabled, false, true, true)]
    [DataRow(ServerKind.Headless, RemoteAccessMode.Unrestricted, false, false, true)]
    [DataRow(ServerKind.Headless, RemoteAccessMode.Disabled, true, false, true)]
    public void RealUserscriptEndpointsKeepTheOrdinaryAuthorizationBoundary(ServerKind kind,
        RemoteAccessMode mode, bool loopback, bool paired, bool allowed)
    {
        var remote = new RemoteAccessContext
        {
            IsLoopback = loopback, Mode = mode,
            Device = paired ? new RemoteDevice {Id = "paired-userscript-device"} : null
        };
        foreach (var action in new[] {nameof(TampermonkeyController.Install), nameof(TampermonkeyController.GetScript)})
        {
            var context = Build(action, remote, typeof(TampermonkeyController));
            new RemoteAccessAuthorizationFilter(new ServerSelfDescription(() => kind)).OnAuthorization(context);
            Assert.AreEqual(allowed, context.Result == null, action);
            if (!allowed)
            {
                Assert.AreEqual(403, ((ObjectResult) context.Result!).StatusCode, action);
                Assert.AreEqual(nameof(RemoteAccessDenialReason.HostOnly), DenialReason(context), action);
            }
        }
        // The same caller still cannot open the server's desktop. The opt-in belongs
        // only to an action that actually implements the browser alternative.
        var native = Build(nameof(ToolController.Open), remote, typeof(ToolController));
        new RemoteAccessAuthorizationFilter(new ServerSelfDescription(() => kind)).OnAuthorization(native);
        Assert.AreEqual(kind == ServerKind.Desktop && loopback, native.Result == null);
        if (kind == ServerKind.Headless || !loopback)
            Assert.AreEqual(nameof(RemoteAccessDenialReason.RunsOnUserMachine), DenialReason(native));
    }

    [TestMethod]
    public void BrowserFallbackIsExplicitAndDoesNotTrustAMissingContext()
    {
        var install = typeof(TampermonkeyController).GetMethod(nameof(TampermonkeyController.Install))!
            .GetCustomAttribute<RunsOnUserMachineAttribute>();
        Assert.IsNotNull(install);
        Assert.IsTrue(install.HasBrowserFallback);
        Assert.IsFalse(new RunsOnUserMachineAttribute().HasBrowserFallback);
        foreach (var action in new[] {nameof(TampermonkeyController.Install), nameof(TampermonkeyController.GetScript)})
        {
            var context = Build(action, null, typeof(TampermonkeyController));
            new RemoteAccessAuthorizationFilter(new ServerSelfDescription(() => ServerKind.Headless)).OnAuthorization(context);
            Assert.AreEqual(nameof(RemoteAccessDenialReason.HostOnly), DenialReason(context), action);
        }
    }

    [TestMethod]
    public void A_missing_context_fails_closed()
    {
        // The middleware not having run is not a reason to trust the caller.
        var context = Build(nameof(Actions.Unmarked), null);

        new RemoteAccessAuthorizationFilter().OnAuthorization(context);

        Assert.AreEqual(nameof(RemoteAccessDenialReason.HostOnly), DenialReason(context));
    }

    [TestMethod]
    public void The_refusal_carries_the_reason_written_on_the_action()
    {
        var context = Build(nameof(Actions.LaunchesAPlayer), Remote(RemoteAccessMode.Unrestricted));

        new RemoteAccessAuthorizationFilter().OnAuthorization(context);

        var response = (BaseResponse) ((ObjectResult) context.Result!).Value!;
        StringAssert.Contains(response.Message, "sitting at");
    }
}
