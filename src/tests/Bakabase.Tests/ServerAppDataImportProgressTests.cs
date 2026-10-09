using Bakabase.Service.Components.ServerData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ServerAppDataImportProgressTests
{
    private string _root = null!;
    private string Source => Path.Combine(_root, "desktop");
    private string Target => Path.Combine(_root, "server");

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-import-progress-" + Guid.NewGuid().ToString("N"));
        CreateData(Source);
        CreateData(Target);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void ReportsExactCopyTotalsIntermediateChunksAndLifecycleWithoutCopyingStatusFiles()
    {
        var payload = new byte[3 * 1024 * 1024 + 17];
        new Random(42).NextBytes(payload);
        File.WriteAllBytes(Path.Combine(Source, "payload.bin"), payload);
        File.WriteAllText(Path.Combine(Source, "empty.txt"), "");
        const string status = ".bakabase-import-status.json";
        foreach (var name in new[] { status, status + ".tmp" })
        {
            File.WriteAllText(Path.Combine(Source, name), "source control file");
            File.WriteAllText(Path.Combine(Target, name), "target control file");
        }
        Directory.CreateDirectory(Path.Combine(Source, "backups"));
        File.WriteAllText(Path.Combine(Source, "backups", "old.txt"), "excluded backup");
        var totalBytes = new[] { "app.json", "bakabase_insideworld.db", "payload.bin", "empty.txt" }
            .Sum(name => new FileInfo(Path.Combine(Source, name)).Length);
        var updates = new List<AppDataImportProgressUpdate>();

        ServerAppDataImport.Queue(Source, Target);
        var id = ServerAppDataImport.ReadPending(Target)!.Id;
        ServerAppDataImport.ApplyPending(Target, report: updates.Add);

        var phases = updates.Where((update, index) => index == 0 || update.Phase != updates[index - 1].Phase)
            .Select(update => update.Phase).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "scanning", "copying", "verifying", "backing-up", "installing", "verifying", "starting"
        }, phases);
        var scanned = updates.Last(update => update.Phase == "scanning");
        Assert.AreEqual(totalBytes, scanned.TotalBytes);
        Assert.AreEqual(4, scanned.TotalFiles);
        Assert.AreEqual(0L, scanned.CompletedBytes);
        var copying = updates.Where(update => update.Phase == "copying").ToArray();
        Assert.IsTrue(copying.All(update => update.TotalBytes == totalBytes && update.TotalFiles == 4));
        Assert.AreEqual(totalBytes, copying[^1].CompletedBytes);
        Assert.AreEqual(4, copying[^1].CompletedFiles);
        for (var index = 1; index < copying.Length; index++)
        {
            var bytesWritten = copying[index].CompletedBytes - copying[index - 1].CompletedBytes;
            Assert.IsTrue(bytesWritten is >= 0 and <= 1024 * 1024);
            Assert.IsTrue(copying[index].CompletedFiles >= copying[index - 1].CompletedFiles);
        }
        var payloadUpdates = copying.Where(update => update.CurrentFile == "payload.bin").ToArray();
        Assert.IsTrue(payloadUpdates.Select(update => update.CompletedBytes).Distinct().Count() >= 5,
            "A large file must report its intermediate chunks, not only file completion.");
        Assert.IsTrue(payloadUpdates.Skip(1).Any(update => update.CompletedFiles == payloadUpdates[0].CompletedFiles &&
                                                         update.CompletedBytes > payloadUpdates[0].CompletedBytes));
        Assert.IsTrue(updates.Where(update => update.CurrentFile != null)
            .All(update => !Path.IsPathFullyQualified(update.CurrentFile!)));
        foreach (var phase in new[] { "backing-up", "installing" })
        {
            var finished = updates.Last(update => update.Phase == phase);
            Assert.IsTrue(finished.TotalEntries > 0);
            Assert.AreEqual(finished.TotalEntries, finished.CompletedEntries);
        }
        Assert.IsTrue(updates.Any(update => update.Phase == "verifying" &&
                                            update.CurrentFile == "bakabase_insideworld.db" &&
                                            update.CompletedEntries == update.TotalEntries && update.TotalEntries > 0));
        Assert.AreEqual(totalBytes, updates[^1].CompletedBytes);
        Assert.AreEqual(4, updates[^1].CompletedFiles);
        CollectionAssert.AreEqual(payload, File.ReadAllBytes(Path.Combine(Target, "payload.bin")));
        foreach (var name in new[] { status, status + ".tmp" })
        {
            Assert.AreEqual("source control file", File.ReadAllText(Path.Combine(Source, name)));
            Assert.AreEqual("target control file", File.ReadAllText(Path.Combine(Target, name)));
            Assert.IsFalse(File.Exists(Path.Combine(Target, ServerAppDataImport.BackupsName, id, name)));
        }
    }

    [TestMethod]
    public void CorruptDatabaseStopsAtVerificationWithoutReportingStarting()
    {
        File.WriteAllText(Path.Combine(Source, "bakabase_insideworld.db"), "not a database");
        var updates = new List<AppDataImportProgressUpdate>();
        ServerAppDataImport.Queue(Source, Target);

        Assert.ThrowsExactly<SqliteException>(() => ServerAppDataImport.ApplyPending(Target, report: updates.Add));

        Assert.AreEqual("verifying", updates[^1].Phase);
        Assert.AreEqual("bakabase_insideworld.db", updates[^1].CurrentFile);
        Assert.IsFalse(updates.Any(update => update.Phase is "backing-up" or "installing" or "starting"));
        Assert.AreEqual("queued", ServerAppDataImport.ReadPending(Target)!.Phase);
        Assert.IsTrue(File.Exists(Path.Combine(Target, "app.json")));
    }

    [TestMethod]
    public void CancellationBetweenChunksPreservesTheJournalAndDestinationForAFullCopyRetry()
    {
        var sourcePayload = new byte[3 * 1024 * 1024];
        new Random(42).NextBytes(sourcePayload);
        File.WriteAllBytes(Path.Combine(Source, "payload.bin"), sourcePayload);
        File.WriteAllText(Path.Combine(Target, "target-only.txt"), "original target");
        ServerAppDataImport.Queue(Source, Target);
        using var cancellation = new CancellationTokenSource();
        var updates = new List<AppDataImportProgressUpdate>();

        Assert.ThrowsExactly<OperationCanceledException>(() => ServerAppDataImport.ApplyPending(Target, report: update =>
        {
            updates.Add(update);
            if (update.Phase == "copying" && update.CurrentFile == "payload.bin" && update.CompletedBytes >= 1024 * 1024)
                cancellation.Cancel();
        }, cancellationToken: cancellation.Token));

        Assert.AreEqual("queued", ServerAppDataImport.ReadPending(Target)!.Phase);
        Assert.AreEqual("original target", File.ReadAllText(Path.Combine(Target, "target-only.txt")));
        Assert.IsFalse(updates.Any(update => update.Phase is "backing-up" or "installing" or "starting"));
        Assert.IsTrue(updates[^1].CompletedBytes < updates[^1].TotalBytes);
        CollectionAssert.AreEqual(sourcePayload, File.ReadAllBytes(Path.Combine(Source, "payload.bin")));

        ServerAppDataImport.ApplyPending(Target);

        Assert.IsNull(ServerAppDataImport.ReadPending(Target));
        CollectionAssert.AreEqual(sourcePayload, File.ReadAllBytes(Path.Combine(Target, "payload.bin")));
    }

    [TestMethod]
    public void InterruptedInstallationReportsRemainingLifecycleWithoutInventingCopyTotals()
    {
        ServerAppDataImport.Queue(Source, Target);
        Assert.ThrowsExactly<IOException>(() => ServerAppDataImport.ApplyPending(Target, report: update =>
        {
            if (update.Phase == "installing" && update.CompletedEntries == 1)
                throw new IOException("simulated interruption after the first root move");
        }));
        Assert.AreEqual("installing", ServerAppDataImport.ReadPending(Target)!.Phase);
        Directory.Delete(Source, true);
        var updates = new List<AppDataImportProgressUpdate>();

        ServerAppDataImport.ApplyPending(Target, report: updates.Add);

        Assert.AreEqual("installing", updates[0].Phase);
        Assert.AreEqual("starting", updates[^1].Phase);
        Assert.IsTrue(updates.All(update => update.CompletedBytes == 0 && update.TotalBytes == 0 &&
                                           update.CompletedFiles == 0 && update.TotalFiles == 0));
        var installed = updates.Last(update => update.Phase == "installing");
        Assert.AreEqual(installed.TotalEntries, installed.CompletedEntries);
        Assert.IsNull(ServerAppDataImport.ReadPending(Target));
    }

    private static void CreateData(string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "app.json"), "{\"App\":{\"version\":\"1.0.0\"}}");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(path, "bakabase_insideworld.db"), Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE sample (value TEXT); INSERT INTO sample VALUES ('original');";
        command.ExecuteNonQuery();
    }
}
