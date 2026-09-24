using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Modules.Federation;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// A device key is full control of another server. It lives in the managed-server file and
/// nowhere else: not in anything the app's API returns, not in anything a relay tells the
/// other server's page, not in a log line.
/// </summary>
[TestClass]
public class ConsoleKeySecrecyTests
{
    private ConsoleHarness _console = null!;
    private FakeServer _desk = null!;
    private FakeServer _nas = null!;
    private string[] _keys = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _console = await ConsoleHarness.StartAsync();
        _desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        _nas = await FakeServer.StartAsync("server-nas", "NAS", 46900);

        // One paired the way a user pairs — the key arrives over the wire and is stored — and
        // one as if imported.
        _desk.PairingCode = "123456";
        await _console.Manager.PairAsync(_desk.BaseAddress, "123456");
        var (_, nasKey) = await _console.AddManagedAsync(_nas);

        _keys = [_console.Store.Find(_desk.ServerId)!.DeviceKey, nasKey];
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();
        await _desk.DisposeAsync();
        await _nas.DisposeAsync();
    }

    private void AssertNoKey(string text, string what)
    {
        foreach (var key in _keys)
        {
            StringAssert.DoesNotMatch(text, new Regex(Regex.Escape(key)), what);
        }

        StringAssert.DoesNotMatch(text, new Regex("deviceKey", RegexOptions.IgnoreCase), what);
    }

    [TestMethod]
    public async Task Nothing_a_relay_answers_its_page_carries_a_key()
    {
        var port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);

        (HttpMethod Method, string Path, string? Body)[] calls =
        [
            (HttpMethod.Get, "/client/status", null),
            (HttpMethod.Get, "/client/switcher", null),
            (HttpMethod.Post, $"/client/switcher/{_nas.ServerId}/open", "{\"path\":\"/\"}"),
            (HttpMethod.Post, "/client/switcher/local/open", null),
            (HttpMethod.Post, "/client/switcher/nobody/open", null),
            (HttpMethod.Put, $"/client/servers/{_desk.ServerId}/path-mappings",
                "{\"mappings\":[{\"serverPath\":\"/a\",\"localPath\":\"/b\"}]}"),
            (HttpMethod.Put, $"/client/servers/{_nas.ServerId}/path-mappings", "{\"mappings\":[]}"),
            (HttpMethod.Post, "/client/tray", "{\"running\":true}"),
            (HttpMethod.Get, RelayPaths.ConnectPath, null),
            (HttpMethod.Post, "/client/connect", "{\"address\":\"127.0.0.1:1\"}"),
            (HttpMethod.Post, "/client/pair/code", "{\"address\":\"127.0.0.1:1\",\"code\":\"1\"}"),
            (HttpMethod.Get, "/client/discover", null),
            (HttpMethod.Get, "/client/migration-hints", null),
            (HttpMethod.Post, "/client/migration-hints/export", null),
            (HttpMethod.Get, "/client/updater/state", null),
            (HttpMethod.Get, "/client/app/info", null),
            (HttpMethod.Post, "/client/app/open?directory=data", "{}"),
            (HttpMethod.Get, "/client/log", null),
            (HttpMethod.Post, "/client/log/open", "{}"),
            (HttpMethod.Get, "/remote-access/context", null),
            (HttpMethod.Get, "/resource/search", null)
        ];

        foreach (var (method, path, body) in calls)
        {
            var response = await ConsoleHarness.SendToRelayAsync(port, path, method, body);
            var raw = await response.Content.ReadAsStringAsync();

            AssertNoKey(raw, $"{method} {path}");
            AssertNoKey(string.Join("\n", response.Headers.Concat(response.Content.Headers)
                .Select(h => $"{h.Key}: {string.Join(",", h.Value)}")), $"{method} {path} headers");

            // This device's own log and data directory are where its keys, and its server's
            // pairing codes, are found — so a relay has no route to either. Asserted here
            // rather than read off the body: this harness has no application service, and a
            // route that did answer would say only "no log here". The same routes against a
            // real log are ConsoleDiagnosticsExposureTests.
            if (path.StartsWith("/client/log", StringComparison.Ordinal) ||
                path.StartsWith("/client/app/", StringComparison.Ordinal))
            {
                Assert.AreEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode, $"{method} {path}: {raw}");
                StringAssert.Contains(raw, "\"message\":\"NotFound\"", $"{method} {path}");
            }
        }
    }

    [TestMethod]
    public async Task No_view_the_app_serves_carries_a_key_in_any_serializer()
    {
        var views = new object?[]
        {
            await _console.Manager.GetAsync(false),
            await _console.Manager.GetAsync(true),
            await _console.Manager.ProbeAsync(_desk.BaseAddress),
            await _console.Manager.PairAsync(_nas.BaseAddress, null),
            await _console.Manager.OpenAsync(_desk.ServerId, "/"),
            await _console.Manager.ImportFromLegacyClientAsync(),
            _console.Manager.ListTargets(),
            await _console.Manager.ResolveUrlAsync(_nas.ServerId)
        };

        foreach (var view in views)
        {
            // The controller writes these with the federation options; the other two are
            // what anything else in the app would reach for.
            AssertNoKey(JsonSerializer.Serialize(view, FederationJson.Options), view?.GetType().Name ?? "null");
            AssertNoKey(JsonSerializer.Serialize(view), view?.GetType().Name ?? "null");
            AssertNoKey(Newtonsoft.Json.JsonConvert.SerializeObject(view), view?.GetType().Name ?? "null");
        }
    }

    [TestMethod]
    public async Task No_log_line_carries_a_key()
    {
        // Everything that touches a key: pairing (in setup), probing, opening, forwarding,
        // re-pairing, forgetting.
        await _console.Manager.GetAsync(true);
        var port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);
        await ConsoleHarness.SendToRelayAsync(port, "/resource/search");
        await ConsoleHarness.SendToRelayAsync(port, "/resource/search", HttpMethod.Post, "{}");
        await _console.Manager.PairAsync(_desk.BaseAddress, "123456");
        _keys = [.. _keys, _console.Store.Find(_desk.ServerId)!.DeviceKey];
        await _console.Manager.ForgetAsync(_nas.ServerId);

        List<string> logs;
        lock (_console.Logs)
        {
            logs = [.. _console.Logs];
        }

        Assert.IsTrue(logs.Count > 0, "nothing was logged, so nothing was checked");
        AssertNoKey(string.Join("\n", logs), "logs");
    }

    [TestMethod]
    public void The_file_holding_the_keys_is_its_owners_alone()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows has no Unix file mode; the profile's ACL protects the file there.");
        }

        Assert.IsTrue(File.Exists(_console.ManagedFile));
        Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_console.ManagedFile));
        Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(_console.ManagedDirectory));

        // No temporary copy left beside it with wider permissions.
        CollectionAssert.AreEquivalent(new[] {_console.ManagedFile}, Directory.GetFiles(_console.ManagedDirectory));
    }
}
