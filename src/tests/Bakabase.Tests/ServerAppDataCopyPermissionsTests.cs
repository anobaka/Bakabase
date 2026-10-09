using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Service.Components.ServerData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ServerAppDataCopyPermissionsTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void CopyPreservesPrivateDataAndKeepsTheWorkingCopyWritable(bool relocation, bool privateSourceRoot)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix access modes are not supported on Windows.");
        const UnixFileMode ownerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        const UnixFileMode ownerDir = ownerFile | UnixFileMode.UserExecute;
        const UnixFileMode publicDir = ownerDir | UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                      UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        var root = ServerSetupSession.Canonical(Path.Combine(Path.GetTempPath(), "bakabase-copy-permissions-" + Guid.NewGuid().ToString("N")));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        try
        {
            File.WriteAllText(Path.Combine(source, "app.json"), "{\"App\":{\"version\":\"1.0.0\"}}");
            var db = Path.Combine(source, "bakabase_insideworld.db");
            using (var connection = new SqliteConnection($"Data Source={db};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Sample(Value TEXT); INSERT INTO Sample VALUES('retained');";
                command.ExecuteNonQuery();
            }
            var privateDirectory = Path.Combine(source, "remote-access", "managed");
            Directory.CreateDirectory(privateDirectory);
            File.SetUnixFileMode(privateDirectory, ownerDir);
            var secret = Path.Combine(privateDirectory, "connection.json");
            File.WriteAllText(secret, "private signing key fixture");
            File.SetUnixFileMode(secret, ownerFile);
            var emptyPrivate = Path.Combine(source, "empty-private");
            Directory.CreateDirectory(emptyPrivate);
            File.SetUnixFileMode(emptyPrivate, ownerDir);
            var groupFile = Path.Combine(source, "group-readable.txt");
            File.WriteAllText(groupFile, "retain explicitly granted group read");
            File.SetUnixFileMode(groupFile, ownerFile | UnixFileMode.GroupRead);
            var executable = Path.Combine(source, "tool");
            File.WriteAllText(executable, "executable fixture");
            var executableMode = UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.SetUser;
            File.SetUnixFileMode(executable, executableMode);
            File.SetUnixFileMode(db, UnixFileMode.UserRead);
            File.SetUnixFileMode(Path.Combine(source, "app.json"), UnixFileMode.UserRead);
            File.SetUnixFileMode(source, privateSourceRoot ? ownerDir : publicDir);
            File.SetUnixFileMode(target, privateSourceRoot ? publicDir : ownerDir);
            if (!relocation) File.WriteAllText(Path.Combine(target, "old-data.txt"), "must remain private in backup");

            using var targetLock = Acquire(target);
            using var sourceLock = relocation ? Acquire(source) : null;
            using var monitor = new ImportProgressStore(relocation ? source : target);
            var id = "";
            var work = Path.Combine(target, relocation ? ServerAppDataRelocation.WorkName : ServerAppDataImport.WorkName);
            var sawPrivateStaging = false;
            void Report(AppDataImportProgressUpdate update)
            {
                monitor.Report(update);
                if (update.Phase != "copying" || update.CompletedBytes == 0) return;
                Assert.AreEqual(ownerDir, File.GetUnixFileMode(target));
                Assert.AreEqual(ownerDir, File.GetUnixFileMode(work));
                Assert.AreEqual(ownerDir, File.GetUnixFileMode(Path.Combine(work, id)));
                var stagedSecret = Path.Combine(work, id, "remote-access", "managed", "connection.json");
                if (!File.Exists(stagedSecret)) return;
                Assert.AreEqual(ownerFile, File.GetUnixFileMode(stagedSecret));
                Assert.AreEqual(ownerDir, File.GetUnixFileMode(Path.GetDirectoryName(stagedSecret)!));
                sawPrivateStaging = true;
            }
            if (relocation)
            {
                var journal = ServerAppDataRelocation.Queue(source, source, target);
                id = journal.Id;
                monitor.BeginRelocation(journal);
                ServerAppDataRelocation.ApplyPending(source, sourceLock!, targetLock, report: Report);
            }
            else
            {
                ServerAppDataImport.Queue(source, target);
                var journal = ServerAppDataImport.ReadPending(target)!;
                id = journal.Id;
                monitor.Begin(journal);
                ServerAppDataImport.ApplyPending(target, report: Report);
                var backupRoot = Path.Combine(target, ServerAppDataImport.BackupsName);
                Assert.AreEqual(ownerDir, File.GetUnixFileMode(backupRoot));
                Assert.AreEqual(ownerDir, File.GetUnixFileMode(Path.Combine(backupRoot, id)));
                Assert.AreEqual("must remain private in backup", File.ReadAllText(Path.Combine(backupRoot, id, "old-data.txt")));
            }
            Assert.IsTrue(sawPrivateStaging, "The copy was never observed with private staging permissions.");
            Assert.AreEqual(ownerDir, File.GetUnixFileMode(target));
            Assert.AreEqual(ownerDir, File.GetUnixFileMode(Path.Combine(target, "remote-access", "managed")));
            Assert.AreEqual(ownerDir, File.GetUnixFileMode(Path.Combine(target, "empty-private")));
            Assert.AreEqual(ownerFile, File.GetUnixFileMode(Path.Combine(target, "remote-access", "managed", "connection.json")));
            Assert.AreEqual(ownerFile | UnixFileMode.GroupRead, File.GetUnixFileMode(Path.Combine(target, "group-readable.txt")));
            Assert.AreEqual(ownerDir, File.GetUnixFileMode(Path.Combine(target, "tool")), "Preserve executable bits, but discard setuid.");
            Assert.AreEqual(ownerFile, File.GetUnixFileMode(Path.Combine(target, "app.json")));
            Assert.AreEqual(ownerFile, File.GetUnixFileMode(Path.Combine(target, "bakabase_insideworld.db")));
            Assert.AreEqual(UnixFileMode.UserRead, File.GetUnixFileMode(db));
            Assert.AreEqual(executableMode, File.GetUnixFileMode(executable));
            Assert.AreEqual(ownerFile, File.GetUnixFileMode(secret));
            Assert.AreEqual("private signing key fixture", File.ReadAllText(secret));
        }
        finally { Directory.Delete(root, true); }
    }

    private static DataDirectoryLock Acquire(string path)
    {
        var attempt = DataDirectoryLock.TryAcquire(path);
        Assert.IsTrue(attempt.Acquired, attempt.Error?.Message);
        return attempt.Lock!;
    }
}
