using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Console;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// Bringing the removed thin client's pairings over, keys included, so nobody has to pair
/// again after switching to the desktop app.
/// </summary>
[TestClass]
public class LegacyClientImportTests
{
    /// <summary>
    /// A <c>connection.json</c> as the thin client writes it: PascalCase, enums by name,
    /// nulls omitted, its own server list with keys. Verbatim rather than produced by the
    /// current writer, so a change to that writer cannot quietly move the target this
    /// import has to hit.
    /// </summary>
    private const string ThinClientFile = """
        {
          "Servers": [
            {
              "ServerId": "server-desk",
              "ServerName": "Desk",
              "BaseAddress": "http://192.168.1.5:34567",
              "DeviceId": "legacy-device-desk",
              "DeviceKey": "legacy-key-desk",
              "PairedAt": "2025-11-02T08:30:00Z",
              "LastConnectedAt": "2026-03-01T10:00:00Z",
              "PathMappings": [
                { "ServerPath": "/data/media", "LocalPath": "Z:\\media" }
              ]
            },
            {
              "ServerId": "server-nas",
              "ServerName": "NAS (from the thin client)",
              "BaseAddress": "http://192.168.1.9:34567",
              "DeviceId": "legacy-device-nas",
              "DeviceKey": "legacy-key-nas",
              "PairedAt": "2025-10-01T00:00:00Z",
              "PathMappings": []
            },
            {
              "ServerId": "this-device",
              "ServerName": "Me",
              "BaseAddress": "http://127.0.0.1:34567",
              "DeviceId": "legacy-device-self",
              "DeviceKey": "legacy-key-self",
              "PairedAt": "2025-10-01T00:00:00Z",
              "PathMappings": []
            },
            {
              "ServerId": "server-broken",
              "BaseAddress": "http://192.168.1.10:34567",
              "PairedAt": "2025-10-01T00:00:00Z"
            }
          ],
          "ActiveServerId": "server-desk",
          "DeviceName": "Old laptop name",
          "Platform": "MacOS"
        }
        """;

    private string _root = null!;
    private string _legacyFile = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-legacy-import", Guid.NewGuid().ToString("N"));
        _legacyFile = Path.Combine(_root, "Bakabase.Client", "client", ClientConnectionStore.FileName);

        Directory.CreateDirectory(Path.GetDirectoryName(_legacyFile)!);
        File.WriteAllText(_legacyFile, ThinClientFile);
    }

    [TestCleanup]
    public void Cleanup() => ConsoleHarness.DeleteRoot(_root);

    private string ConsoleRoot => Path.Combine(_root, "app");

    [TestMethod]
    public async Task Pairings_come_over_on_first_start_without_touching_what_is_already_here()
    {
        // The NAS was already paired in the app, with a newer key and mappings of its own.
        // Stopped rather than disposed: disposing a harness deletes its data, and the point
        // is the next launch finding it. Cleanup removes the whole root afterwards.
        var seeded = await ConsoleHarness.StartAsync(ConsoleRoot);
        try
        {
            await seeded.Store.MutateAsync(data => data.Servers.Add(new ClientServerConnection
            {
                ServerId = "server-nas",
                ServerName = "NAS",
                BaseAddress = "http://192.168.1.9:34567",
                DeviceId = "app-device-nas",
                DeviceKey = "app-key-nas",
                PairedAt = DateTime.UtcNow,
                PathMappings = [new ClientPathMapping {ServerPath = "/volume1", LocalPath = "/Volumes/volume1"}]
            }));
        }
        finally
        {
            await seeded.StopAsync();
        }

        var before = await File.ReadAllBytesAsync(_legacyFile);

        await using var console = await ConsoleHarness.StartAsync(ConsoleRoot, _legacyFile, importOnStart: true);
        var data = console.Store.Read();

        var desk = data.Servers.Single(s => s.ServerId == "server-desk");
        Assert.AreEqual("legacy-device-desk", desk.DeviceId);
        Assert.AreEqual("legacy-key-desk", desk.DeviceKey);
        Assert.AreEqual("http://192.168.1.5:34567", desk.BaseAddress);
        Assert.AreEqual("Z:\\media", desk.PathMappings.Single().LocalPath);
        Assert.AreEqual(new DateTime(2025, 11, 2, 8, 30, 0, DateTimeKind.Utc), desk.PairedAt.ToUniversalTime());
        Assert.IsTrue(desk.ImportedFromLegacyClient);

        var nas = data.Servers.Single(s => s.ServerId == "server-nas");
        Assert.AreEqual("app-key-nas", nas.DeviceKey);
        Assert.AreEqual("NAS", nas.ServerName);
        Assert.AreEqual("/volume1", nas.PathMappings.Single().ServerPath);
        Assert.IsFalse(nas.ImportedFromLegacyClient);

        // This device's own server, and an entry with no key: nothing to manage there.
        Assert.IsFalse(data.Servers.Any(s => s.ServerId is "this-device" or "server-broken"));

        Assert.IsNotNull(data.LegacyClientImportedAt);
        Assert.AreEqual("Old laptop name", data.DeviceName);

        // Read, never written: the thin client finds its file exactly as it left it.
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(_legacyFile));

        var view = (await console.Manager.GetAsync(false)).Servers.Single(s => s.ServerId == "server-desk");
        Assert.IsTrue(view.ImportedFromLegacyClient);
    }

    [TestMethod]
    public async Task The_automatic_import_runs_once_and_a_manual_one_runs_again()
    {
        // Stopped rather than disposed, which would delete the data the next launch reads.
        var first = await ConsoleHarness.StartAsync(ConsoleRoot, _legacyFile, importOnStart: true);
        try
        {
            Assert.IsNotNull(first.Store.Find("server-desk"));

            // Stopped managing it afterwards. Nothing answers at that address, which is
            // fine: forgetting does not depend on the server agreeing.
            await first.Store.MutateAsync(data => data.Servers.RemoveAll(s => s.ServerId == "server-desk"));
        }
        finally
        {
            await first.StopAsync();
        }

        await using var second = await ConsoleHarness.StartAsync(ConsoleRoot, _legacyFile, importOnStart: true);

        // Not resurrected on the next launch.
        Assert.IsNull(second.Store.Find("server-desk"));

        var manual = await second.Manager.ImportFromLegacyClientAsync();

        Assert.IsTrue(manual.Found);
        Assert.AreEqual(1, manual.Imported);
        Assert.AreEqual(3, manual.Skipped);
        Assert.AreEqual("legacy-key-desk", second.Store.Find("server-desk")!.DeviceKey);
    }

    [TestMethod]
    public async Task No_thin_client_means_nothing_found_and_nothing_written()
    {
        File.Delete(_legacyFile);

        await using var console = await ConsoleHarness.StartAsync(ConsoleRoot, _legacyFile, importOnStart: true);

        Assert.AreEqual(new Bakabase.Modules.RemoteAccess.Abstractions.Models.ManagedServerImportView(false, 0, 0),
            await console.Manager.ImportFromLegacyClientAsync());
        Assert.IsFalse(File.Exists(console.ManagedFile));
        Assert.IsNull(console.Store.Read().LegacyClientImportedAt);
    }

    [TestMethod]
    public async Task A_corrupt_thin_client_file_is_nothing_found()
    {
        await File.WriteAllTextAsync(_legacyFile, "{ not json");

        await using var console = await ConsoleHarness.StartAsync(ConsoleRoot, _legacyFile, importOnStart: true);

        Assert.IsFalse((await console.Manager.ImportFromLegacyClientAsync()).Found);
        Assert.AreEqual(0, console.Store.Read().Servers.Count);
    }

    [TestMethod]
    public void The_thin_clients_file_is_found_where_the_thin_client_kept_it()
    {
        var expected = DefaultAppDataPathResolver.Resolve(AppDataPathProfile.Client, CurrentPlatform(),
            Environment.GetEnvironmentVariable, Environment.GetFolderPath,
            LegacyClientConnectionSource.ClientExecutableName,
#if DEBUG
            true
#else
            false
#endif
        );

        var resolved = LegacyClientConnectionSource.ResolveDefaultFile();

        Assert.IsNotNull(resolved);
        StringAssert.EndsWith(resolved, Path.Combine("client", ClientConnectionStore.FileName));

        // Under the client profile's anchor, or wherever that anchor redirects to — never
        // under the app's own.
        if (!AnchorRedirect.Exists(expected))
        {
            Assert.AreEqual(Path.Combine(expected, "client", ClientConnectionStore.FileName), resolved);
        }

        Assert.IsFalse(resolved.StartsWith(AppService.DefaultAppDataDirectory + Path.DirectorySeparatorChar,
            StringComparison.Ordinal));
    }

    private static OSPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? OSPlatform.Windows :
        OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
}
