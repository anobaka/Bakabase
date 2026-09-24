using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Remoting.Abstractions;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Console;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// Where the keys to every managed server live, and the one-entry view each relay gets
/// instead of that list.
/// </summary>
[TestClass]
public class ManagedServerStoreTests
{
    private string _root = null!;
    private ManagedServerStore _store = null!;

    private sealed class TempDirectory(string path) : IClientDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-managed-store", Guid.NewGuid().ToString("N"));
        _store = new ManagedServerStore(new ManagedServerDirectory(() => Path.Combine(_root, "managed")));

        await _store.MutateAsync(data =>
        {
            data.DeviceName = "Laptop";
            data.Servers.Add(Entry("server-a", "key-a"));
            data.Servers.Add(Entry("server-b", "key-b"));
        });
    }

    [TestCleanup]
    public void Cleanup() => ConsoleHarness.DeleteRoot(_root);

    private static ClientServerConnection Entry(string id, string key) => new()
    {
        ServerId = id,
        ServerName = id.ToUpperInvariant(),
        BaseAddress = $"http://{id}:34567",
        DeviceId = $"device-{id}",
        DeviceKey = key,
        PairedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        PathMappings = [new ClientPathMapping {ServerPath = "/data", LocalPath = $"/mnt/{id}"}]
    };

    [TestMethod]
    public void A_relay_sees_only_its_own_server_as_the_active_one()
    {
        var view = new SingleServerConnectionStore(_store, "server-a");
        var data = view.Read();

        Assert.AreEqual("server-a", data.Servers.Single().ServerId);
        Assert.AreEqual("server-a", data.ActiveServerId);
        Assert.AreEqual("Laptop", data.DeviceName);

        // What the relay core signs with is that server's key and nothing else.
        var connection = new ActiveConnection(view);
        Assert.AreEqual("device-server-a", connection.Current!.DeviceId);
        Assert.AreEqual("key-a", connection.Current.Key);
    }

    [TestMethod]
    public async Task A_relay_can_record_mappings_and_contact_for_its_own_server_and_nothing_else()
    {
        var view = new SingleServerConnectionStore(_store, "server-a");
        var connectedAt = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);

        await view.MutateAsync(data =>
        {
            var own = data.Servers.Single();

            // The two things a relay legitimately records...
            own.PathMappings = [new ClientPathMapping {ServerPath = "/media", LocalPath = "Z:\\media"}];
            own.LastConnectedAt = connectedAt;

            // ...and everything it must not be able to do.
            own.DeviceKey = "stolen";
            own.BaseAddress = "http://evil:1";
            own.RelayPort = 1;
            data.Servers.Add(Entry("server-c", "key-c"));
            data.ActiveServerId = "server-c";
            data.DeviceName = "Renamed";
        });

        var stored = _store.Read();

        Assert.AreEqual(2, stored.Servers.Count, "a server was added through the view");
        Assert.AreEqual("Laptop", stored.DeviceName);
        Assert.IsNull(stored.ActiveServerId);

        var a = stored.Servers.Single(s => s.ServerId == "server-a");
        Assert.AreEqual("key-a", a.DeviceKey);
        Assert.AreEqual("http://server-a:34567", a.BaseAddress);
        Assert.IsNull(a.RelayPort);
        Assert.AreEqual(connectedAt, a.LastConnectedAt);
        Assert.AreEqual("/media", a.PathMappings.Single().ServerPath);

        var b = stored.Servers.Single(s => s.ServerId == "server-b");
        Assert.AreEqual("/mnt/server-b", b.PathMappings.Single().LocalPath);
    }

    [TestMethod]
    public async Task The_thin_clients_own_operations_cannot_reach_past_the_view()
    {
        var connection = new ActiveConnection(new SingleServerConnectionStore(_store, "server-a"));

        // Another server's mappings: not visible, so not found.
        Assert.IsFalse(await connection.SetPathMappingsAsync("server-b",
            [new ClientPathMapping {ServerPath = "/x", LocalPath = "/y"}]));

        // Forgetting or switching through the view changes nothing underneath.
        await connection.ForgetAsync("server-a");
        await connection.ActivateAsync("server-b");

        // Saving a pairing through the view replaces nothing: the key stays, and the
        // mappings are not wiped by the replacement's empty list.
        await connection.SaveAsync("server-a", "A", "http://elsewhere:1",
            new ClientCredentials("device-x", "key-x"), DateTime.UtcNow);

        var a = _store.Find("server-a")!;
        Assert.AreEqual("key-a", a.DeviceKey);
        Assert.AreEqual("/mnt/server-a", a.PathMappings.Single().LocalPath);
        Assert.AreEqual("/mnt/server-b", _store.Find("server-b")!.PathMappings.Single().LocalPath);
        Assert.AreEqual(2, _store.Read().Servers.Count);
    }

    [TestMethod]
    public async Task A_view_of_a_server_that_was_forgotten_is_empty()
    {
        var view = new SingleServerConnectionStore(_store, "server-a");
        Assert.AreEqual(1, view.Read().Servers.Count);

        await _store.MutateAsync(data => data.Servers.RemoveAll(s => s.ServerId == "server-a"));

        Assert.AreEqual(0, view.Read().Servers.Count);
        Assert.IsNull(view.Read().ActiveServerId);
        Assert.IsNull(new ActiveConnection(view).Current);
    }

    [TestMethod]
    public async Task What_a_reader_holds_does_not_change_under_it()
    {
        var before = _store.Read();

        await _store.MutateAsync(data => data.Servers.Add(Entry("server-c", "key-c")));

        // Readers get a snapshot; the next write publishes a new one instead of editing
        // the list somebody may be enumerating.
        Assert.AreEqual(2, before.Servers.Count);
        Assert.AreEqual(3, _store.Read().Servers.Count);
    }

    [TestMethod]
    public async Task A_restart_reads_back_what_was_written()
    {
        await _store.MutateAsync(data => data.Servers[0].RelayPort = 34651);

        var reopened = new ManagedServerStore(new ManagedServerDirectory(() => Path.Combine(_root, "managed")));

        Assert.AreEqual(34651, reopened.Find("server-a")!.RelayPort);
        Assert.AreEqual("key-b", reopened.Find("server-b")!.DeviceKey);
    }

    [TestMethod]
    public void The_managed_file_is_readable_by_its_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows has no Unix file mode; the profile's ACL protects the file there.");
        }

        var file = Path.Combine(_root, "managed", ClientConnectionStore.FileName);

        Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.Combine(_root, "managed")));
    }

    [TestMethod]
    public async Task The_thin_clients_file_is_tightened_on_its_next_write_too()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows has no Unix file mode; the profile's ACL protects the file there.");
        }

        var directory = Path.Combine(_root, "client");
        Directory.CreateDirectory(directory);

        // Written before this rule existed: world-readable, and a stale temp file beside it.
        var file = Path.Combine(directory, ClientConnectionStore.FileName);
        await File.WriteAllTextAsync(file, "{\"Servers\":[]}");
        await File.WriteAllTextAsync(file + ".tmp", "{}");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead |
                                   UnixFileMode.OtherRead);
        File.SetUnixFileMode(file + ".tmp", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

        var store = new ClientConnectionStore(new TempDirectory(directory));
        await store.MutateAsync(data => data.Servers.Add(Entry("server-a", "key-a")));

        Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        Assert.AreEqual("key-a", new ClientConnectionStore(new TempDirectory(directory)).Read().Servers.Single().DeviceKey);
    }
}
