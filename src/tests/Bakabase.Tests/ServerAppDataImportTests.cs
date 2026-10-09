using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Service.Components.ServerData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests;

[TestClass]
public class ServerAppDataImportTests
{
    private string _root = null!;
    private string Source => Path.Combine(_root, "desktop");
    private string Target => Path.Combine(_root, "server");

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-import-tests-" + Guid.NewGuid().ToString("N"));
        CreateData(Source, "desktop");
        CreateData(Target, "server");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void ImportPreservesSourceAndBacksUpWholeDestination()
    {
        File.WriteAllText(Path.Combine(Source, "app.json"), new JObject
        {
            ["version"] = "1.0.0", ["dataPath"] = "/old/AppData", ["prevDataPath"] = "/older/AppData",
            ["wwwRootPath"] = "/old/application/web"
        }.ToString());
        File.WriteAllText(Path.Combine(Target, "target-only.txt"), "must be backed up, not merged");
        Directory.CreateDirectory(Path.Combine(Target, "backups", "previous-version"));
        File.WriteAllText(Path.Combine(Target, "backups", "previous-version", "keep"), "existing backup");
        var originalConfig = File.ReadAllText(Path.Combine(Source, "app.json"));
        var originalDb = File.ReadAllBytes(Path.Combine(Source, "bakabase_insideworld.db"));
        ServerAppDataImport.Queue(Source, Target, "C:\\old\\AppData");
        var id = ServerAppDataImport.ReadPending(Target)!.Id;
        ServerAppDataImport.ApplyPending(Target);

        Assert.AreEqual("desktop", ReadValue(Target));
        Assert.AreEqual(originalConfig, File.ReadAllText(Path.Combine(Source, "app.json")));
        CollectionAssert.AreEqual(originalDb, File.ReadAllBytes(Path.Combine(Source, "bakabase_insideworld.db")));
        var backup = Path.Combine(Target, ServerAppDataImport.BackupsName, id);
        Assert.AreEqual("server", ReadValue(backup));
        Assert.IsTrue(File.Exists(Path.Combine(backup, "target-only.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(Target, "target-only.txt")));
        Assert.IsNull(ServerAppDataImport.ReadPending(Target));
        var options = JObject.Parse(File.ReadAllText(Path.Combine(Target, "app.json")));
        Assert.AreEqual("1.0.0", (string?)options["version"]);
        Assert.IsNull(options["dataPath"]);
        Assert.IsNull(options["wwwRootPath"]);
        CollectionAssert.Contains(ImportedAppDataRoots.Read(Target), "/older/AppData");
        CollectionAssert.Contains(ImportedAppDataRoots.Read(Target), "C:\\old\\AppData");
        Assert.IsTrue(File.Exists(Path.Combine(Target, "backups", "previous-version", "keep")));
        Assert.IsFalse(Directory.Exists(Path.Combine(backup, "backups")));
        Assert.AreEqual(Path.Combine(Target, "covers/a.jpg").Replace('\\', '/'),
            AppDataPathRelocation.Resolve("/old/AppData/covers/a.jpg", Target, ImportedAppDataRoots.Read(Target)));
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("\"Disabled\"")]
    public void ContainerPreflightPreventsDisabledAccessLockoutWithoutChangingSource(string mode)
    {
        Directory.CreateDirectory(Path.Combine(Source, "configs"));
        var path = Path.Combine(Source, "configs", "remote-access.json");
        var content = $"{{\"RemoteAccess\":{{\"mode\":{mode},\"requirePairing\":true}}}}";
        File.WriteAllText(path, content);
        Assert.ThrowsExactly<IOException>(() => ServerAppDataImport.ValidateContainerAccess(Source, true));
        ServerAppDataImport.ValidateContainerAccess(Source, false); // Native loopback still works.
        Assert.AreEqual(content, File.ReadAllText(path));
    }

    [TestMethod]
    public void ContainerPreflightKeepsEnabledPairingPolicy()
    {
        Directory.CreateDirectory(Path.Combine(Source, "configs"));
        var path = Path.Combine(Source, "configs", "remote-access.json");
        const string content = "{\"RemoteAccess\":{\"mode\":1,\"requirePairing\":true}}";
        File.WriteAllText(path, content);
        ServerAppDataImport.ValidateContainerAccess(Source, true);
        Assert.AreEqual(content, File.ReadAllText(path));
    }

    [TestMethod]
    public void PreservesRealAppOptionsSectionAndRebasesItsHistoricalPaths()
    {
        File.WriteAllText(Path.Combine(Source, "app.json"), "\uFEFF{\"App\":{\"version\":\"1.0.0\",\"prevDataPath\":\"/original/AppData\",\"wwwRootPath\":\"/old/web\"},\"other\":42}");
        ServerAppDataImport.Queue(Source, Target);
        ServerAppDataImport.ApplyPending(Target);
        var document = JObject.Parse(File.ReadAllText(Path.Combine(Target, "app.json")));
        Assert.AreEqual("1.0.0", (string?)document["App"]!["version"]);
        Assert.IsNotNull(document["App"]!["prevDataPath"]);
        Assert.IsNull(document["prevDataPath"]);
        Assert.IsNull(document["App"]!["wwwRootPath"]);
        Assert.AreEqual(42, (int)document["other"]!);
        CollectionAssert.Contains(ImportedAppDataRoots.Read(Target), "/original/AppData");
    }

    [TestMethod]
    public void ImportsCommittedWalWithoutChangingSourceDatabase()
    {
        using var connection = Connection(Source);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; UPDATE example SET value='from wal';";
        command.ExecuteNonQuery();
        var dbPath = Path.Combine(Source, "bakabase_insideworld.db");
        var db = File.ReadAllBytes(dbPath);
        var wal = File.ReadAllBytes(dbPath + "-wal");
        ServerAppDataImport.Queue(Source, Target);
        ServerAppDataImport.ApplyPending(Target);
        Assert.AreEqual("from wal", ReadValue(Target));
        CollectionAssert.AreEqual(db, File.ReadAllBytes(dbPath));
        CollectionAssert.AreEqual(wal, File.ReadAllBytes(dbPath + "-wal"));
    }

    [TestMethod]
    public void KeepsImportedToolsExecutableAndDoesNotOverwriteBackupMetadata()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Path.Combine(Source, "components"));
        var tool = Path.Combine(Source, "components", "tool");
        File.WriteAllText(tool, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(Target, "import-record.json"), "user file");
        ServerAppDataImport.Queue(Source, Target);
        var id = ServerAppDataImport.ReadPending(Target)!.Id;
        ServerAppDataImport.ApplyPending(Target);
        Assert.IsTrue(File.GetUnixFileMode(Path.Combine(Target, "components", "tool")).HasFlag(UnixFileMode.UserExecute));
        Assert.AreEqual("user file", File.ReadAllText(Path.Combine(Target, ServerAppDataImport.BackupsName, id, "import-record.json")));
    }

    [TestMethod]
    public void CorruptDatabaseNeverReplacesCurrentData()
    {
        File.WriteAllText(Path.Combine(Source, "bakabase_insideworld.db"), "not sqlite");
        ServerAppDataImport.Queue(Source, Target);
        Assert.ThrowsExactly<SqliteException>(() => ServerAppDataImport.ApplyPending(Target));
        Assert.AreEqual("server", ReadValue(Target));
        Assert.AreEqual("queued", ServerAppDataImport.ReadPending(Target)!.Phase);
        Assert.IsFalse(Directory.Exists(Path.Combine(Target, ServerAppDataImport.BackupsName)));
    }

    [TestMethod]
    public void RestartResumesAfterBackupWithoutNeedingOriginalSource()
    {
        ServerAppDataImport.Queue(Source, Target);
        var id = ServerAppDataImport.ReadPending(Target)!.Id;
        Assert.ThrowsExactly<IOException>(() => ServerAppDataImport.ApplyPending(Target, message =>
        {
            if (message.StartsWith("Installing")) throw new IOException("simulated interruption");
        }));
        Assert.AreEqual("installing", ServerAppDataImport.ReadPending(Target)!.Phase);
        // Simulate interruption after one of the incoming root entries had moved.
        File.Move(Path.Combine(Target, ServerAppDataImport.WorkName, id, "app.json"), Path.Combine(Target, "app.json"));
        Directory.Delete(Source, true);
        ServerAppDataImport.ApplyPending(Target);
        Assert.AreEqual("desktop", ReadValue(Target));
        Assert.AreEqual("server", ReadValue(Path.Combine(Target, ServerAppDataImport.BackupsName, id)));
    }

    [TestMethod]
    public void RefusesNewerDataAndOverlappingOrLinkedDirectories()
    {
        Assert.IsFalse(ServerAppDataImport.Validate(Target, Target).Valid);
        Assert.IsFalse(ServerAppDataImport.Validate(_root, Target).Valid);
        File.WriteAllText(Path.Combine(Source, "app.json"), "{\"version\":\"999.0.0\"}");
        StringAssert.Contains(ServerAppDataImport.Validate(Source, Target).Error!, "newer");
        if (OperatingSystem.IsWindows()) return;
        var alias = Path.Combine(_root, "alias");
        Directory.CreateSymbolicLink(alias, Target);
        Assert.IsFalse(ServerAppDataImport.Validate(alias, Target).Valid);
    }

    [TestMethod]
    public void RefusesSourceSymlinkBeforeMovingDestination()
    {
        if (OperatingSystem.IsWindows()) return;
        File.CreateSymbolicLink(Path.Combine(Source, "linked.txt"), Path.Combine(Target, "app.json"));
        ServerAppDataImport.Queue(Source, Target);
        Assert.ThrowsExactly<IOException>(() => ServerAppDataImport.ApplyPending(Target));
        Assert.AreEqual("server", ReadValue(Target));
    }

    [TestMethod]
    public void RefusesRunningSourceAndLeavesItsLockMetadataUntouched()
    {
        ServerAppDataImport.Queue(Source, Target);
        var attempt = DataDirectoryLock.TryAcquire(Source);
        using (attempt.Lock)
        {
            Assert.IsTrue(attempt.Acquired);
            var validation = ServerAppDataImport.Validate(Source, Target);
            Assert.IsFalse(validation.Valid);
            StringAssert.Contains(validation.Error!, "in use");
            Assert.ThrowsExactly<IOException>(() => ServerAppDataImport.ApplyPending(Target));
            Assert.AreEqual("server", ReadValue(Target));
        }
        var owner = File.ReadAllText(Path.Combine(Source, DataDirectoryLock.FileName));
        ServerAppDataImport.ApplyPending(Target);
        Assert.AreEqual(owner, File.ReadAllText(Path.Combine(Source, DataDirectoryLock.FileName)));
    }

    [TestMethod]
    public void RedirectedSourceAndCancellationWorkWithoutModifyingSource()
    {
        var anchor = Path.Combine(_root, "anchor");
        Directory.CreateDirectory(anchor);
        AnchorRedirect.Write(anchor, Source);
        Assert.IsTrue(ServerAppDataImport.Validate(anchor, Target).Valid);
        ServerAppDataImport.Queue(anchor, Target);
        Assert.ThrowsExactly<IOException>(() => ServerAppDataImport.Queue(Source, Target));
        ServerAppDataImport.Cancel(Target);
        Assert.IsNull(ServerAppDataImport.ReadPending(Target));
        Assert.AreEqual(Source, AnchorRedirect.TryRead(anchor));
    }

    private static void CreateData(string path, string value)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "app.json"), "{\"App\":{\"version\":\"1.0.0\"}}");
        using var connection = Connection(path);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE example (value TEXT); INSERT INTO example VALUES ($value);";
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static string ReadValue(string path)
    {
        using var connection = Connection(path);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM example";
        return (string)command.ExecuteScalar()!;
    }

    private static SqliteConnection Connection(string path) => new(new SqliteConnectionStringBuilder
    { DataSource = Path.Combine(path, "bakabase_insideworld.db"), Pooling = false }.ToString());
}
