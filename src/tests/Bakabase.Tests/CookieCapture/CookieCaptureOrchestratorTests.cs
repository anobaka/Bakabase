using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components.CookieCapture;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;
using Bakabase.Tests.Implementations;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.CookieCapture;

[TestClass]
public class CookieCaptureOrchestratorTests
{
    [TestMethod]
    public async Task HappyPath_RunsChain_ClearsStale_MirrorsAcrossDomains_AndExtractsCookies()
    {
        var session = new FakeWebViewSession();
        // Forums login already set ipb_* on the e-hentai.org cookie store.
        session.SeedCookie("https://e-hentai.org/", "ipb_member_id", "12345");
        session.SeedCookie("https://e-hentai.org/", "ipb_pass_hash", "hash");
        // After visiting e-hentai.org the server sets igneous (we'll add it before that hop).
        var orchestrator = NewOrchestrator(session);

        var flow = new FakeCookieCaptureFlow
        {
            StartUrl = "https://forums.e-hentai.org/index.php?act=Login&CODE=00",
            CookieUrls = ["https://exhentai.org/", "https://forums.e-hentai.org/", "https://e-hentai.org/"],
            StaleCookiesImpl = [("https://exhentai.org/", "yay")],
            CookieMirrorsImpl = [
                new CookieMirror("https://e-hentai.org/", ".exhentai.org",
                    ["ipb_member_id", "ipb_pass_hash", "igneous"]),
            ],
            GetNextUrlImpl = url =>
            {
                var uri = new Uri(url);
                if (uri.Host == "forums.e-hentai.org" && uri.Query.Contains("CODE=01"))
                    return "https://e-hentai.org/";
                if (uri.Host == "e-hentai.org") return "https://exhentai.org/";
                return null;
            },
        };

        var captureTask = orchestrator.CaptureAsync(flow);

        // Login form load: chain should NOT trigger (CODE=00).
        await session.RaiseNavigatedAsync("https://forums.e-hentai.org/index.php?act=Login&CODE=00");

        // User submits → CODE=01 success page: chain triggers forums → e-hentai.
        await session.RaiseNavigatedAsync("https://forums.e-hentai.org/index.php?act=Login&CODE=01");

        // Server sets igneous on .e-hentai.org once we land there.
        session.SeedCookie("https://e-hentai.org/", "igneous", "ig-token");
        await session.RaiseNavigatedAsync("https://e-hentai.org/");

        // After mirror, exhentai.org should serve real content. Seed exhentai's own cookie.
        session.SeedCookie("https://exhentai.org/", "yay", "louder-stale");
        // (Stale yay was just deleted by the chain hop pre-clear; seeding here represents a
        // cookie the server set on the actual exhentai.org load — but for this test we just
        // verify final extraction sees the mirrored ones, not the seeded yay.)
        await session.RaiseNavigatedAsync("https://exhentai.org/");

        session.Confirm();
        var result = await captureTask;

        result.Should().NotBeNull();
        result.Should().Contain("ipb_member_id=12345");
        result.Should().Contain("ipb_pass_hash=hash");
        result.Should().Contain("igneous=ig-token");

        // Stale cookies got wiped on init AND before each chain hop (3 hops = 1 init + 2 chain).
        var staleClears = session.Operations.Count(o => o == "DeleteCookie:https://exhentai.org/|yay");
        staleClears.Should().BeGreaterOrEqualTo(2,
            "stale cookies are wiped at init AND before each chain hop for defense in depth");

        // Chain only navigated 2 explicit hops (e-hentai.org and exhentai.org), plus the StartUrl.
        var navigates = session.Operations.Where(o => o.StartsWith("Navigate:")).ToList();
        navigates.Should().BeEquivalentTo(new[]
        {
            "Navigate:https://forums.e-hentai.org/index.php?act=Login&CODE=00",
            "Navigate:https://e-hentai.org/",
            "Navigate:https://exhentai.org/",
        }, options => options.WithStrictOrdering());
    }

    [TestMethod]
    public async Task PreInit_StaleCookiesAreDeletedBeforeFirstNavigate()
    {
        var session = new FakeWebViewSession();
        var orchestrator = NewOrchestrator(session);

        var flow = new FakeCookieCaptureFlow
        {
            StartUrl = "https://example.com/",
            StaleCookiesImpl = [("https://example.com/", "blocker")],
            GetNextUrlImpl = _ => null,
        };

        var captureTask = orchestrator.CaptureAsync(flow);
        session.Confirm();
        await captureTask;

        var deleteIndex = session.Operations.FindIndex(o => o == "DeleteCookie:https://example.com/|blocker");
        var firstNavIndex = session.Operations.FindIndex(o => o.StartsWith("Navigate:"));

        deleteIndex.Should().BeGreaterOrEqualTo(0, "stale cookie should be cleared");
        firstNavIndex.Should().BeGreaterOrEqualTo(0, "start URL should be navigated");
        deleteIndex.Should().BeLessThan(firstNavIndex,
            "stale cookie clear must happen before first navigation");
    }

    [TestMethod]
    public async Task ChainDedups_SameTargetNavigatedAtMostOnce()
    {
        var session = new FakeWebViewSession();
        var orchestrator = NewOrchestrator(session);

        var flow = new FakeCookieCaptureFlow
        {
            StartUrl = "https://a.example/",
            // Both `b.example` and `c.example` say "go to https://target.example/" — should
            // dedup so target is navigated only once.
            GetNextUrlImpl = url =>
            {
                var uri = new Uri(url);
                return uri.Host is "b.example" or "c.example" ? "https://target.example/" : null;
            },
        };

        var captureTask = orchestrator.CaptureAsync(flow);
        await session.RaiseNavigatedAsync("https://a.example/");
        await session.RaiseNavigatedAsync("https://b.example/");  // triggers nav to target.example
        await session.RaiseNavigatedAsync("https://c.example/");  // dedup'd — does NOT re-navigate
        session.Confirm();
        await captureTask;

        var targetNavCount = session.Operations.Count(o => o == "Navigate:https://target.example/");
        targetNavCount.Should().Be(1, "chain must dedup repeated targets");
    }

    [TestMethod]
    public async Task ChainHop_OrdersDeleteBeforeMirrorBeforeNavigate()
    {
        var session = new FakeWebViewSession();
        session.SeedCookie("https://source.example/", "auth", "v");
        var orchestrator = NewOrchestrator(session);

        var flow = new FakeCookieCaptureFlow
        {
            StartUrl = "https://source.example/",
            StaleCookiesImpl = [("https://target.example/", "block")],
            CookieMirrorsImpl = [new CookieMirror("https://source.example/", ".target.example", ["auth"])],
            GetNextUrlImpl = url =>
                new Uri(url).Host == "source.example" ? "https://target.example/" : null,
        };

        var captureTask = orchestrator.CaptureAsync(flow);
        await session.RaiseNavigatedAsync("https://source.example/");
        session.Confirm();
        await captureTask;

        // Find indices of the chain-hop operations (the second occurrence of DeleteCookie
        // is the chain-hop pre-clear; the first is the pre-init drain).
        var ops = session.Operations;
        var initDelete = ops.IndexOf("DeleteCookie:https://target.example/|block");
        var chainDelete = ops.IndexOf("DeleteCookie:https://target.example/|block", initDelete + 1);
        var mirror = ops.IndexOf("Mirror:https://source.example/->.target.example|[auth]");
        var nav = ops.IndexOf("Navigate:https://target.example/");

        chainDelete.Should().BeGreaterThan(initDelete, "chain-hop pre-clear runs after init drain");
        mirror.Should().BeGreaterThan(chainDelete, "mirror runs after stale clear");
        nav.Should().BeGreaterThan(mirror, "navigate runs after mirror has set up cross-domain cookies");
    }

    [TestMethod]
    public async Task UserCancels_ReturnsNull()
    {
        var session = new FakeWebViewSession();
        var orchestrator = NewOrchestrator(session);

        var flow = new FakeCookieCaptureFlow
        {
            StartUrl = "https://example.com/",
            GetNextUrlImpl = _ => null,
        };

        var captureTask = orchestrator.CaptureAsync(flow);
        session.Cancel();
        var result = await captureTask;

        result.Should().BeNull("user cancel should surface as null result");
        session.Operations.Should().NotContain(o => o.StartsWith("GetCookies:"),
            "cookies should not be extracted after cancel");
    }

    [TestMethod]
    public async Task HandlerException_IsIsolated_DoesNotCrashSession()
    {
        var session = new FakeWebViewSession();
        var orchestrator = NewOrchestrator(session);

        var flow = new FakeCookieCaptureFlow
        {
            StartUrl = "https://example.com/",
            GetNextUrlImpl = url =>
            {
                if (url.Contains("trigger-throw")) throw new InvalidOperationException("kaboom");
                return null;
            },
        };

        var captureTask = orchestrator.CaptureAsync(flow);

        await session.RaiseNavigatedAsync("https://example.com/trigger-throw");
        // After the buggy handler, more navigations + confirm should still work fine.
        await session.RaiseNavigatedAsync("https://example.com/another");
        session.Confirm();

        var result = await captureTask;

        result.Should().BeNull("no cookies seeded, but extraction completes — proves session survived");
        session.SwallowedHandlerExceptions.Should().HaveCount(1);
        session.SwallowedHandlerExceptions.Single().Should().BeOfType<InvalidOperationException>();
    }

    [TestMethod]
    public async Task SerializedNavigated_HandlersDoNotInterleave()
    {
        // Tests the serialization invariant directly on the session (not through the
        // orchestrator) so the assertion is unambiguous: two concurrent RaiseNavigatedAsync
        // calls must run their handlers strictly one-after-another. We verify by recording
        // entry/exit order — interleaved would show A-in, B-in, A-out, B-out; serialized
        // shows A-in, A-out, B-in, B-out.
        var session = new FakeWebViewSession();
        var log = new List<string>();
        var entryGate = new TaskCompletionSource();

        session.OnNavigated(async url =>
        {
            lock (log) log.Add($"in:{url}");
            if (url.Contains("first")) await entryGate.Task;
            lock (log) log.Add($"out:{url}");
        });

        // Start both on the threadpool so the test thread isn't blocked while the first
        // handler waits on the gate.
        var firstRaise = Task.Run(() => session.RaiseNavigatedAsync("first"));
        var secondRaise = Task.Run(() => session.RaiseNavigatedAsync("second"));

        // Let the first raise enter the handler and start awaiting the gate; only then
        // release it. If serialization were broken, the second raise would have already
        // recorded "in:second" before this point.
        await Task.Delay(100);
        entryGate.SetResult();
        await Task.WhenAll(firstRaise, secondRaise);

        log.Should().BeEquivalentTo(new[]
        {
            "in:first",
            "out:first",
            "in:second",
            "out:second",
        }, options => options.WithStrictOrdering(),
        "Navigated handlers must serialize so chain logic doesn't race");

        await session.DisposeAsync();
    }

    private static CookieCaptureOrchestrator NewOrchestrator(FakeWebViewSession session)
    {
        return new CookieCaptureOrchestrator(new FakeGuiAdapter(session), new TestBakabaseLocalizer());
    }
}

internal sealed class FakeGuiAdapter(FakeWebViewSession session) : IGuiAdapter
{
    public IWebViewSession CreateWebViewSession(WebViewSessionOptions options) => session;

    public void ShowFatalErrorWindow(string message, string title = "Fatal Error") => throw new NotImplementedException();
    public void ShowInitializationWindow(string processName, string? detail = null, double? fraction = null) => throw new NotImplementedException();
    public void DestroyInitializationWindow() => throw new NotImplementedException();
    public void ShowMainWebView(string url, string title, Func<Task> onClosing) => throw new NotImplementedException();
    public void SetMainWindowTitle(string title) => throw new NotImplementedException();
    public bool MainWebViewVisible => throw new NotImplementedException();
    public void Shutdown() => throw new NotImplementedException();
    public void Hide() => throw new NotImplementedException();
    public void Show() => throw new NotImplementedException();
    public void ShowConfirmationDialogOnFirstTimeExiting(Func<CloseBehavior, bool, Task> onClosed) => throw new NotImplementedException();
    public bool ShowConfirmDialog(string message, string caption) => throw new NotImplementedException();
    public void ChangeUiTheme(UiTheme theme) => throw new NotImplementedException();
    public byte[]? GetIcon(IconType type, string? path) => throw new NotImplementedException();
}

internal sealed class FakeCookieCaptureFlow : ICookieCaptureFlow
{
    public CookieValidatorTarget Target { get; init; } = CookieValidatorTarget.ExHentai;
    public string StartUrl { get; init; } = "https://example.com/";
    public string PlatformName { get; init; } = "Test";
    public string[] CookieUrls { get; init; } = ["https://example.com/"];

    public Func<string, string?>? GetNextUrlImpl { get; init; }
    public (string Url, string Name)[] StaleCookiesImpl { get; init; } = [];
    public CookieMirror[] CookieMirrorsImpl { get; init; } = [];

    public string? GetNextUrl(string currentUrl) => GetNextUrlImpl?.Invoke(currentUrl);
    public (string Url, string Name)[] StaleCookiesToClear => StaleCookiesImpl;
    public CookieMirror[] CookieMirrors => CookieMirrorsImpl;
}
