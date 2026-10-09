using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Service.Components.ServerData;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class SetupStoragePolicyTests
{
    private string _root = null!, _data = null!, _source = null!, _outside = null!;
    private UserStoragePolicy _storage = null!;

    [TestInitialize]
    public void Initialize()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Linux container mount fixtures use POSIX paths.");
        _root = ServerSetupSession.Canonical(Path.Combine(Path.GetTempPath(), "bakabase-setup-storage-" + Guid.NewGuid().ToString("N")));
        _data = Path.Combine(_root, "data");
        _source = Path.Combine(_root, "source");
        _outside = Path.Combine(_root, "container-files");
        foreach (var path in new[] {_data, _source, _outside}) Directory.CreateDirectory(path);
        _storage = new UserStoragePolicy(true, () =>
            $"1 0 0:1 / / rw - overlay overlay rw\n2 1 8:1 /data {_data} rw - ext4 /dev/test rw\n" +
            $"3 1 8:1 /source {_source} ro - ext4 /dev/test rw\n", () => _data);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public void SetupShowsMountedAppDataAndReadOnlySourceWithoutExposingContainerRoot()
    {
        var result = ServerSetupDirectoryBrowser.Read(_data, null, null, default, _storage);
        Assert.IsTrue(result.IsRestricted);
        Assert.AreEqual("", result.CurrentPath);
        Assert.IsNull(result.ParentPath);
        CollectionAssert.AreEquivalent(new[] {_data, _source}, result.Roots.Select(r => r.Path).ToArray());
        Assert.IsTrue(result.Roots.Single(r => r.Path == _source).ReadOnly);
        Assert.IsFalse(_storage.GetRoots().Any(r => r.Path == _data), "AppData stays out of ordinary tools.");
        var source = ServerSetupDirectoryBrowser.Read(_data, _source, "new-folder", default, _storage);
        Assert.AreEqual(Path.Combine(_source, "new-folder"), source.CandidatePath,
            "Read-only is descriptive: this read-only picker must not probe or reject writes.");
        Assert.IsFalse(Directory.Exists(source.CandidatePath));
    }

    [TestMethod]
    public void MountedRootCannotNavigateToItsUnsharedParentOrSelectAnOutsideLink()
    {
        Directory.CreateSymbolicLink(Path.Combine(_source, "escape"), _outside);
        Directory.CreateDirectory(Path.Combine(_source, "allowed"));
        var result = ServerSetupDirectoryBrowser.Read(_data, _source, null, default, _storage);
        Assert.IsNull(result.ParentPath);
        CollectionAssert.AreEqual(new[] {"allowed"}, result.Directories.Select(d => d.Name).ToArray());
        Assert.ThrowsExactly<UserStoragePathException>(() => ServerSetupDirectoryBrowser.Read(_data, _outside, null, default, _storage));
        Assert.ThrowsExactly<UserStoragePathException>(() => ServerSetupDirectoryBrowser.Read(_data, Path.Combine(_source, "escape"), null, default, _storage));
    }

    [TestMethod]
    public void NativeServerUsesOrdinaryDirectoriesWithoutAMountManifest()
    {
        var native = new UserStoragePolicy(false);
        var result = ServerSetupDirectoryBrowser.Read(_data, _outside, null, default, native);
        Assert.IsFalse(result.IsRestricted);
        Assert.AreEqual(_outside, result.CurrentPath);
        Assert.AreEqual(_root, result.ParentPath);
        Assert.IsTrue(result.Roots.Length > 0);
    }

    [TestMethod]
    public void SetupRejectsUnsharedSourceBeforeReadingItOrQueuingAnImport()
    {
        using var dataLock = Acquire();
        using var session = new ServerSetupSession(_data, _data, true, dataLock, true, _storage);
        var request = new ServerSetupSession.SetupRequest {SourcePath = _outside, Operation = "import"};
        var result = session.Validate(request, session.Token);
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(result.Error!, "mounted storage location");
        Assert.ThrowsExactly<IOException>(() => session.Commit(request, session.Token));
        Assert.IsNull(ServerAppDataImport.ReadPending(_data));
        Assert.IsFalse(File.Exists(Path.Combine(_data, "bakabase_insideworld.db")));
    }

    [TestMethod]
    public void ImportMappingTargetsUseTheSameStorageBoundary()
    {
        File.WriteAllText(Path.Combine(_source, "app.json"), JsonSerializer.Serialize(new
            {App = new {Version = ServerAppDataImport.RunningVersion.ToString()}}));
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_source, "bakabase_insideworld.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Example(Id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }
        using var dataLock = Acquire();
        using var session = new ServerSetupSession(_data, _data, true, dataLock, true, _storage);
        Assert.IsTrue(session.Validate(new() {SourcePath = _source}, session.Token).Valid);
        var invalid = session.Validate(new()
        {
            SourcePath = _source,
            PathMappings = [new PathMappingRule("Z:/Media", _outside)]
        }, session.Token);
        Assert.IsFalse(invalid.Valid);
        StringAssert.Contains(invalid.Error!, "mounted storage location");
    }

    private DataDirectoryLock Acquire()
    {
        var attempt = DataDirectoryLock.TryAcquire(_data);
        Assert.IsTrue(attempt.Acquired, attempt.Error?.Message);
        return attempt.Lock!;
    }
}
