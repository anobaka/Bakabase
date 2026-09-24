using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// A managed server's page never learns anything about this device beyond what the console
/// contract hands it: not its log, not its data directories, and it cannot open them.
/// </summary>
/// <remarks>
/// <para>
/// The removed thin client published its own log and directories to the page it showed
/// (<c>/client/log*</c>, <c>/client/app/*</c>), because there they were only the thin
/// client's. In the desktop app the same routes would be this device's: the log of its own
/// server — including the pairing code that server prints while it waits for its first
/// device, which is full control of this device — and the data directory that holds every
/// managed server's key. A managed server's older UI may still ask for them.
/// </para>
/// <para>
/// Everything here runs with the app's real <see cref="AppService"/> bridged into the relay
/// and a real log on disk, so a route that answered would have something to answer with: a
/// test against a relay with no application service or no log proves nothing.
/// </para>
/// </remarks>
[TestClass]
public class ConsoleDiagnosticsExposureTests
{
    private const string PairingCode = "482913";

    /// <summary>What <see cref="AppService"/>'s own file sink writes, character for character.</summary>
    private const string AppLogTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] ({SourceContext}.{Method}) {Message}{NewLine}{Exception}";

    private static readonly FieldInfo DefaultDirectoryField =
        typeof(AppService).GetField("_defaultAppDataDirectory", BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException(
            "AppService no longer caches its data directory in _defaultAppDataDirectory; point this test at it anew.");

    private string _root = null!;
    private object? _previousDirectory;
    private string? _previousVariable;
    private string _variable = null!;
    private AppService _app = null!;
    private ConsoleHarness _console = null!;
    private FakeServer _nas = null!;

    [TestInitialize]
    public async Task Setup()
    {
        // The static constructor settles where this process's data is, and every other test
        // that touches AppService runs it with the environment as it is; run it the same way
        // here, before pointing the app at a directory of this test's own.
        RuntimeHelpers.RunClassConstructor(typeof(AppService).TypeHandle);

        _root = Path.Combine(Path.GetTempPath(), "bakabase-console-diagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _variable = AppDataAnchor.Current.EnvVarName;
        _previousVariable = Environment.GetEnvironmentVariable(_variable);
        _previousDirectory = DefaultDirectoryField.GetValue(null);

        // BAKABASE_DATA_DIR, as a headless install or a portable one sets it, re-read once.
        Environment.SetEnvironmentVariable(_variable, _root);
        DefaultDirectoryField.SetValue(null, null);

        _app = new AppService(NullLogger<AppService>.Instance, null!, new ServiceCollection().BuildServiceProvider());

        Assert.AreEqual(Path.GetFullPath(_root), AppService.DefaultAppDataDirectory,
            "the app is not looking at this test's directory, so nothing below would be about it");

        WriteAppLog(_app.AppInfo.LogPath);

        _console = await ConsoleHarness.StartAsync(services: s => s.AddSingleton(_app));
        _nas = await FakeServer.StartAsync("server-nas", "NAS", 46900);
        await _console.AddManagedAsync(_nas);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_console != null)
        {
            await _console.DisposeAsync();
        }

        if (_nas != null)
        {
            await _nas.DisposeAsync();
        }

        DefaultDirectoryField.SetValue(null, _previousDirectory);
        Environment.SetEnvironmentVariable(_variable, _previousVariable);
        ConsoleHarness.DeleteRoot(_root);
    }

    /// <summary>
    /// This device's log as the app's own file sink writes it: its server announcing a first-
    /// device pairing code, and the console naming another server it manages.
    /// </summary>
    private static void WriteAppLog(string logDirectory)
    {
        using (var logger = new LoggerConfiguration()
                   .Enrich.FromLogContext()
                   .WriteTo.File(Path.Combine(logDirectory, "AppLog_.log"), rollingInterval: RollingInterval.Day,
                       outputTemplate: AppLogTemplate)
                   .CreateLogger())
        {
            logger.ForContext("SourceContext", "Bakabase.Service.Components.RemoteAccess.FirstDevicePairingCodeAnnouncer")
                .Information("Enter this code on the first device: {Code} (valid for {Minutes} minutes)", PairingCode,
                    10);
            logger.ForContext("SourceContext", "Bakabase.Remoting.Components.Console.RemoteConsoleManager")
                .Information("Now managing {ServerName} ({ServerId}) at {Address}", "Other NAS", "server-other",
                    "http://192.168.1.77:34567");
        }

        // What a route that published the log would hand out — so an empty answer below is
        // the relay refusing, not a log that happened to be empty.
        var written = string.Join("\n", Directory.EnumerateFiles(logDirectory, "*.log").Select(ReadShared));
        StringAssert.Contains(written, PairingCode);
    }

    /// <summary>Reads a log file the way a reader beside a live logger has to: sharing it.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [TestMethod]
    public async Task A_relay_never_answers_with_this_devices_log_paths_or_folder_opener()
    {
        var port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_nas.ServerId, null))!.Url);

        // Every route the thin client published about the machine it ran on, in every shape
        // a page could ask — the read first, so a relay that answered stops the test before
        // the openers below could reach this machine's file manager.
        (HttpMethod Method, string Path)[] calls =
        [
            (HttpMethod.Get, "/client/log"),
            (HttpMethod.Get, "/client/log?take=50"),
            (HttpMethod.Get, "/client/log?contains=Enter%20this%20code&level=Information"),
            (HttpMethod.Get, "/client/app/info"),
            (HttpMethod.Post, "/client/log/open"),
            (HttpMethod.Post, "/client/app/open?directory=data"),
            (HttpMethod.Post, "/client/app/open?directory=log"),
            (HttpMethod.Post, "/client/app/open?directory=components")
        ];

        var forwardedBefore = _nas.Requests.Count;

        foreach (var (method, path) in calls)
        {
            var response = await ConsoleHarness.SendToRelayAsync(port, path, method,
                method == HttpMethod.Post ? "{}" : null);
            var body = await response.Content.ReadAsStringAsync();
            var what = $"{method} {path}";

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, $"{what}: {body}");

            foreach (var secret in new[]
                     {
                         PairingCode, "Enter this code", "Now managing", "server-other", "192.168.1.77", _root,
                         Path.GetFileName(_root), "available", "entries", "opened", "dataDirectory", "logDirectory"
                     })
            {
                Assert.IsFalse(body.Contains(secret, StringComparison.OrdinalIgnoreCase),
                    $"{what} answered with '{secret}': {body}");
            }
        }

        // Refused here, not handed to the server: /client is never the server's prefix.
        Assert.IsFalse(_nas.Requests.Skip(forwardedBefore).Any(r => r.Path.StartsWith("/client", StringComparison.Ordinal)),
            string.Join("\n", _nas.Requests.Select(r => $"{r.Method} {r.Path}")));

        // The relay still works, and still answers what the contract gives the page.
        var status = await ConsoleHarness.SendToRelayAsync(port, "/client/status");
        Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
        var statusBody = await status.Content.ReadAsStringAsync();
        StringAssert.Contains(statusBody, "\"host\":\"console\"");
        Assert.IsFalse(statusBody.Contains(_root, StringComparison.OrdinalIgnoreCase), statusBody);
    }

    [TestMethod]
    public async Task The_relay_has_this_apps_service_so_the_refusal_is_not_for_want_of_one()
    {
        // The relay was composed with the real application service; the routes above are
        // missing by decision, not because the relay had nothing to answer them with.
        await _console.Manager.OpenAsync(_nas.ServerId, null);

        Assert.AreSame(_app, _console.Manager.RelayServices(_nas.ServerId)!.GetRequiredService<AppService>());
    }
}
