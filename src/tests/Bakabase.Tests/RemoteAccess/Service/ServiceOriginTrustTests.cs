using System.Net;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bootstrap.Models.Constants;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Service;

/// <summary>
/// Which pages this Service trusts depends on the build: <c>yarn dev</c>'s server only in a
/// development one. And whatever the build, a browser is told who may frame its pages.
/// </summary>
/// <remarks>
/// The tests themselves run in a development build, so a packaged build's answer is asked
/// for by runtime rather than read from <see cref="AppService.RuntimeMode"/> — and then
/// checked end to end, through the real CORS middleware and the real gates.
/// </remarks>
[TestClass]
public class ServiceOriginTrustTests
{
    private const string Attacker = "https://attacker.example";

    private static readonly RuntimeMode[] PackagedRuntimes = [RuntimeMode.WinForms, RuntimeMode.MacOS, RuntimeMode.Docker];

    [TestMethod]
    public void The_dev_server_is_read_from_the_same_place_cors_adds_it_from()
    {
        CollectionAssert.AreEqual(new[] {ServiceGateHost.DevOrigin}, ServiceCorsOrigins.DevServerOrigins.ToArray());
    }

    [TestMethod]
    public void Only_a_development_build_trusts_the_dev_server()
    {
        string[] endpoints = ["http://localhost:34567"];

        Assert.IsTrue(new ServiceCorsOrigins(RuntimeMode.Dev).TrustsDevServer);
        Assert.IsTrue(new ServiceCorsOrigins(RuntimeMode.Dev).Build(endpoints).IsOriginAllowed(ServiceGateHost.DevOrigin));

        foreach (var runtime in PackagedRuntimes)
        {
            var origins = new ServiceCorsOrigins(runtime);
            var policy = origins.Build(endpoints);

            Assert.IsFalse(origins.TrustsDevServer, runtime.ToString());
            Assert.IsFalse(policy.IsOriginAllowed(ServiceGateHost.DevOrigin), runtime.ToString());
            CollectionAssert.DoesNotContain(policy.Origins.ToList(), ServiceGateHost.DevOrigin, runtime.ToString());

            // Nothing else moves with it.
            Assert.IsTrue(policy.IsOriginAllowed(endpoints[0]), runtime.ToString());
            foreach (var site in ServiceCorsOrigins.UserscriptSites)
            {
                Assert.IsTrue(policy.IsOriginAllowed(site), $"{runtime}: {site}");
            }
        }
    }

    [TestMethod]
    public void The_service_runs_with_its_own_builds_answer()
    {
        Assert.AreEqual(AppService.RuntimeMode == RuntimeMode.Dev, ServiceCorsOrigins.ForThisBuild.TrustsDevServer);
    }

    [TestMethod]
    public void Frames_are_allowed_from_this_origin_and_the_dev_server_only_where_it_is_trusted()
    {
        Assert.AreEqual($"'self' {ServiceGateHost.DevOrigin}", new ServiceCorsOrigins(RuntimeMode.Dev).FrameAncestors);
        foreach (var runtime in PackagedRuntimes)
        {
            Assert.AreEqual("'self'", new ServiceCorsOrigins(runtime).FrameAncestors, runtime.ToString());
        }
    }

    [TestMethod]
    public async Task A_packaged_build_refuses_the_dev_server_in_cors_and_in_the_guard()
    {
        await using var host = await ServiceGateHost.StartAsync([typeof(GuardProbeController)],
            origins: new ServiceCorsOrigins(RuntimeMode.WinForms));

        // CORS: the preflight gets no allowance, so the browser never sends the write.
        var preflight = host.Request(HttpMethod.Options, "/test-probe/write", "same-site", ServiceGateHost.DevOrigin);
        preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        Assert.IsFalse((await host.SendAsync(preflight)).Headers.Contains("Access-Control-Allow-Origin"));

        // The guard: a write that needs no preflight, and a GET that runs on this machine.
        foreach (var site in new[] {"same-site", "cross-site"})
        {
            Assert.AreEqual(HttpStatusCode.Forbidden,
                (await host.SendAsync(HttpMethod.Post, "/test-probe/write", site, ServiceGateHost.DevOrigin)).StatusCode,
                site);
            Assert.AreEqual(HttpStatusCode.Forbidden,
                (await host.SendAsync(HttpMethod.Get, "/test-probe/open", site, ServiceGateHost.DevOrigin)).StatusCode,
                site);
        }

        // Nor may it frame this device's UI.
        Assert.AreEqual(HttpStatusCode.Forbidden, (await host.SendAsync(LoopbackCrossSiteGuardTests.FrameLoad(host,
            "/test-probe/read", "iframe", "same-site", ServiceGateHost.DevOrigin))).StatusCode);

        // What a packaged build still trusts, it still trusts.
        var own = await host.SendAsync(HttpMethod.Post, "/test-probe/write", "same-origin", host.Origin);
        Assert.AreEqual(HttpStatusCode.OK, own.StatusCode);
        var userscript = await host.SendAsync(HttpMethod.Post, "/test-probe/write", "cross-site", "https://exhentai.org");
        Assert.AreEqual(HttpStatusCode.OK, userscript.StatusCode);
        var endpoint = await host.SendAsync(HttpMethod.Post, "/test-probe/write", "same-site", $"http://localhost:{host.Port}");
        Assert.AreEqual(HttpStatusCode.OK, endpoint.StatusCode);
    }

    [TestMethod]
    public async Task In_a_packaged_build_the_guard_still_trusts_exactly_the_pages_cors_trusts()
    {
        await using var host = await ServiceGateHost.StartAsync([typeof(GuardProbeController)],
            origins: new ServiceCorsOrigins(RuntimeMode.MacOS));

        foreach (var origin in new[]
                 {
                     ServiceGateHost.DevOrigin, $"http://localhost:{host.Port}", "https://www.north-plus.net",
                     "https://exhentai.org", ServiceGateHost.RelayOrigin, Attacker, "http://127.0.0.1:3000"
                 })
        {
            var preflight = host.Request(HttpMethod.Options, "/test-probe/write", "cross-site", origin);
            preflight.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
            var corsAllows = (await host.SendAsync(preflight)).Headers.Contains("Access-Control-Allow-Origin");

            var guardAllows = (await host.SendAsync(HttpMethod.Post, "/test-probe/write", "cross-site", origin))
                .StatusCode != HttpStatusCode.Forbidden;

            Assert.AreEqual(corsAllows, guardAllows, origin);
        }
    }

    [TestMethod]
    public async Task The_spa_shell_tells_the_browser_only_this_origin_may_frame_it()
    {
        await using var host = await ServiceGateHost.StartAsync([typeof(GuardProbeController)],
            origins: new ServiceCorsOrigins(RuntimeMode.WinForms));

        // The shell, as this device's own window loads it, and as a relay window switching
        // back loads it.
        foreach (var (site, page) in new[] {("same-origin", host.Origin), ("same-site", ServiceGateHost.RelayOrigin)})
        {
            var shell = await host.SendAsync(LoopbackCrossSiteGuardTests.FrameLoad(host, "/", "document", site, page));
            Assert.AreEqual(HttpStatusCode.OK, shell.StatusCode, site);
            AssertFramingHeaders(shell, site);
        }

        // Everything else too, a refusal included — a browser reads them only on a
        // document it is asked to frame, so they cost nothing elsewhere.
        AssertFramingHeaders(await host.SendAsync(HttpMethod.Get, "/test-probe/read"), "an API read");
        var refused = await host.SendAsync(LoopbackCrossSiteGuardTests.FrameLoad(host, "/", "iframe", "same-site",
            ServiceGateHost.RelayOrigin));
        Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
        AssertFramingHeaders(refused, "the guard's refusal");
    }

    [TestMethod]
    public async Task An_endpoints_own_content_security_policy_is_kept_alongside()
    {
        await using var host = await ServiceGateHost.StartAsync([typeof(GuardProbeController)],
            origins: new ServiceCorsOrigins(RuntimeMode.Docker));

        var response = await host.SendAsync(HttpMethod.Get, "/test-probe/sandboxed");
        var policies = response.Headers.GetValues(FrameAncestorsPolicy.ContentSecurityPolicyHeader).ToList();

        CollectionAssert.Contains(policies, "sandbox; default-src 'none'");
        CollectionAssert.Contains(policies, "frame-ancestors 'self'");
    }

    private static void AssertFramingHeaders(HttpResponseMessage response, string because)
    {
        Assert.IsTrue(response.Headers.TryGetValues(FrameAncestorsPolicy.ContentSecurityPolicyHeader, out var csp),
            because);
        CollectionAssert.AreEqual(new[] {"frame-ancestors 'self'"}, csp.ToArray(), because);
        Assert.IsTrue(response.Headers.TryGetValues(FrameAncestorsPolicy.FrameOptionsHeader, out var options), because);
        Assert.AreEqual("SAMEORIGIN", options.Single(), because);
    }
}
