using System.Security.Cryptography;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Service.Components.ServerData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public class ServerAppDataCombinedImportTests
{
    private string _root = null!;
    private string _anchor = null!;
    private string _current = null!;
    private string _source = null!;
    private string _target = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = ServerSetupSession.Canonical(Path.Combine(Path.GetTempPath(), "bakabase-combined-import-" + Guid.NewGuid().ToString("N")));
        _anchor = Path.Combine(_root, "anchor");
        _current = Path.Combine(_root, "current");
        _source = Path.Combine(_root, "external");
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_anchor);
        CreateLibrary(_current, "current");
        CreateLibrary(_source, "imported");
        AnchorRedirect.Write(_anchor, _current);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void CombinedImportUsesOneCapabilityAndPreservesBothOriginalLibraries()
    {
        var currentBytes = LibraryBytes(_current);
        var sourceBytes = LibraryBytes(_source);
        using var currentLock = Lock(_current);
        var plan = Queue();
        Assert.IsFalse(Directory.Exists(_target));
        Assert.AreEqual(2, plan.SchemaVersion);
        using var monitor = Monitor(plan);
        var token = monitor.Token;
        Assert.AreEqual("import", monitor.Read()!.Operation);
        Assert.AreEqual(_current, monitor.Read()!.BackupPath);
        Assert.AreEqual(_source, monitor.Read()!.SourcePath);
        Assert.AreEqual(_target, monitor.Read()!.TargetPath);
        using var targetLock = Lock(_target);
        ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: monitor.Report);
        Assert.AreEqual(_target, AnchorRedirect.TryRead(_anchor));
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_anchor));
        Assert.IsNull(ServerAppDataImport.ReadPending(_target));
        Assert.AreEqual("imported", Value(_target));
        Assert.AreEqual(currentBytes, LibraryBytes(_current));
        Assert.AreEqual(sourceBytes, LibraryBytes(_source));
        Assert.IsTrue(ImportedAppDataRoots.Read(_target).Contains(_source));
        using var transferred = new ImportProgressStore(_target);
        Assert.AreEqual(token, transferred.Token);
        Assert.AreEqual("starting", transferred.Read()!.Phase);
        Assert.AreEqual(_current, transferred.Read()!.BackupPath);
        Assert.IsTrue(transferred.Read()!.TotalBytes > 0);
        Assert.AreEqual(transferred.Read()!.TotalBytes, transferred.Read()!.CompletedBytes);
    }

    [TestMethod]
    public void QueuedCancellationDoesNotCreateOrCleanTheDestination()
    {
        var currentBytes = LibraryBytes(_current);
        var sourceBytes = LibraryBytes(_source);
        Queue();
        ServerAppDataRelocation.Cancel(_anchor);
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_anchor));
        Assert.IsFalse(Directory.Exists(_target));
        Assert.AreEqual(_current, AnchorRedirect.TryRead(_anchor));
        Assert.AreEqual(currentBytes, LibraryBytes(_current));
        Assert.AreEqual(sourceBytes, LibraryBytes(_source));
    }

    [TestMethod]
    public void ValidationRejectsOverlapWithCurrentDestinationAndTheAnchor()
    {
        Assert.IsFalse(ServerAppDataRelocation.ValidateImport(_anchor, _current, _target, _current).Valid);
        Assert.IsFalse(ServerAppDataRelocation.ValidateImport(_anchor, _current, Path.Combine(_source, "child"), _source).Valid);
        Assert.IsFalse(ServerAppDataRelocation.ValidateImport(_anchor, _current, Path.Combine(_current, "child"), _source).Valid);
        var nestedAnchor = Path.Combine(_source, "nested-anchor");
        Directory.CreateDirectory(nestedAnchor);
        AnchorRedirect.Write(nestedAnchor, _current);
        var validation = ServerAppDataRelocation.ValidateImport(nestedAnchor, _current, _target, _source);
        Assert.IsFalse(validation.Valid);
        StringAssert.Contains(validation.Error!, "setup directory");
        Assert.ThrowsException<IOException>(() => ServerAppDataRelocation.QueueImport(nestedAnchor, _current, _target, _source));
        Assert.IsFalse(File.Exists(Path.Combine(nestedAnchor, ServerAppDataRelocation.MarkerName)));
        Assert.IsFalse(Directory.Exists(_target));
    }

    [TestMethod]
    public void InterruptedCopyResumesTheSameInnerOperationAndCannotBeCancelled()
    {
        File.WriteAllBytes(Path.Combine(_source, "large.bin"), new byte[2 * 1024 * 1024]);
        var currentBytes = LibraryBytes(_current);
        var sourceBytes = LibraryBytes(_source);
        using var currentLock = Lock(_current);
        var plan = Queue();
        using var targetLock = Lock(_target);
        using var monitor = Monitor(plan);
        var token = monitor.Token;
        Assert.ThrowsException<SimulatedCrash>(() => ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: update =>
        {
            monitor.Report(update);
            if (update.Phase == "copying" && update.CompletedBytes >= 1024 * 1024) throw new SimulatedCrash();
        }));
        Assert.AreEqual("importing", ServerAppDataRelocation.ReadPending(_anchor)!.Phase);
        Assert.AreEqual(plan.Id, ServerAppDataImport.ReadPending(_target)!.Id);
        Assert.ThrowsException<IOException>(() => ServerAppDataRelocation.Cancel(_anchor));
        Assert.AreEqual(_current, AnchorRedirect.TryRead(_anchor));
        ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: monitor.Report);
        Assert.AreEqual(token, monitor.Token);
        Assert.AreEqual("imported", Value(_target));
        Assert.AreEqual(currentBytes, LibraryBytes(_current));
        Assert.AreEqual(sourceBytes, LibraryBytes(_source));
    }

    [TestMethod]
    public void ACompletedInnerReceiptRecoversBeforeOuterReadyWithoutTheExternalSource()
    {
        using var currentLock = Lock(_current);
        var plan = Queue();
        using var targetLock = Lock(_target);
        using var monitor = Monitor(plan);
        StopAfterInnerImport(currentLock, targetLock, monitor);
        Assert.IsNull(ServerAppDataImport.ReadPending(_target));
        // Exact crash window: receipt and marker deletion persisted, but the inner
        // executor had not removed its now-empty staging directory yet.
        Directory.CreateDirectory(Path.Combine(_target, ServerAppDataImport.WorkName, plan.Id));
        var targetBytes = LibraryBytes(_target);
        Directory.Delete(_source, true);
        ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: monitor.Report);
        Assert.AreEqual(_target, AnchorRedirect.TryRead(_anchor));
        Assert.AreEqual("imported", Value(_target));
        Assert.AreEqual(targetBytes, LibraryBytes(_target));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_target, ServerAppDataImport.BackupsName), "*.json").Length);
    }

    [TestMethod]
    [DataRow("id")]
    [DataRow("source")]
    [DataRow("target")]
    [DataRow("original")]
    public void AReceiptForAnotherOperationCannotCommitThePointer(string mismatch)
    {
        using var currentLock = Lock(_current);
        var plan = Queue();
        using var targetLock = Lock(_target);
        using var monitor = Monitor(plan);
        StopAfterInnerImport(currentLock, targetLock, monitor);
        var path = Path.Combine(_target, ServerAppDataImport.BackupsName, plan.Id + ".json");
        var receipt = JsonConvert.DeserializeObject<ServerAppDataImport.Journal>(File.ReadAllText(path))!;
        if (mismatch == "id") receipt.Id = Guid.NewGuid().ToString("N");
        if (mismatch == "source") receipt.SourcePath = Path.Combine(_root, "other-source");
        if (mismatch == "target") receipt.TargetPath = Path.Combine(_root, "other-target");
        if (mismatch == "original") receipt.OriginalDataPath = "/unexpected-original";
        File.WriteAllText(path, JsonConvert.SerializeObject(receipt));
        Assert.ThrowsException<IOException>(() => ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: monitor.Report));
        Assert.AreEqual(_current, AnchorRedirect.TryRead(_anchor));
        Assert.AreEqual("current", Value(_current));
    }

    [TestMethod]
    public void AForeignInnerMarkerIsNotConsumedOrReplaced()
    {
        using var currentLock = Lock(_current);
        var plan = Queue();
        plan.Phase = "importing";
        Save(plan);
        using var targetLock = Lock(_target);
        ServerAppDataImport.Queue(_source, _target);
        var foreign = File.ReadAllText(Path.Combine(_target, ServerAppDataImport.MarkerName));
        using var monitor = Monitor(plan);
        Assert.ThrowsException<IOException>(() => ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: monitor.Report));
        Assert.AreEqual(foreign, File.ReadAllText(Path.Combine(_target, ServerAppDataImport.MarkerName)));
        Assert.AreEqual(_current, AnchorRedirect.TryRead(_anchor));
    }

    [TestMethod]
    public void ATargetWithFilesButNoPendingImportOrReceiptIsNeverQueuedAgain()
    {
        using var currentLock = Lock(_current);
        var plan = Queue();
        plan.Phase = "importing";
        Save(plan);
        using var targetLock = Lock(_target);
        File.WriteAllText(Path.Combine(_target, "keep.txt"), "unclaimed data");
        using var monitor = Monitor(plan);
        Assert.ThrowsException<IOException>(() => ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: monitor.Report));
        Assert.IsNull(ServerAppDataImport.ReadPending(_target));
        Assert.AreEqual("unclaimed data", File.ReadAllText(Path.Combine(_target, "keep.txt")));
        Assert.AreEqual(_current, AnchorRedirect.TryRead(_anchor));
    }

    [TestMethod]
    public void ReceiptRecoveryStillChecksTheDatabaseBeforeSwitching()
    {
        using var currentLock = Lock(_current);
        var plan = Queue();
        using var targetLock = Lock(_target);
        using var monitor = Monitor(plan);
        StopAfterInnerImport(currentLock, targetLock, monitor);
        File.WriteAllText(Path.Combine(_target, "bakabase_insideworld.db"), "corrupt database");
        Assert.ThrowsException<SqliteException>(() => ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: monitor.Report));
        Assert.AreEqual(_current, AnchorRedirect.TryRead(_anchor));
        Assert.AreEqual("current", Value(_current));
    }

    [TestMethod]
    public void RedirectedReadyReplayKeepsTheTargetCapabilityAndFinishesCleanup()
    {
        using var currentLock = Lock(_current);
        var plan = Queue();
        using var targetLock = Lock(_target);
        using var monitor = Monitor(plan);
        ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: monitor.Report);
        var receipt = ServerAppDataImport.ReadCompletedReceipt(_target, plan.Id, _source, plan.OriginalDataPath)!;
        plan.Phase = "ready";
        plan.IncomingEntries = receipt.IncomingEntries.Concat(new[] { "backups" }).ToArray();
        Save(plan);
        monitor.Dispose();
        using var targetMonitor = new ImportProgressStore(_target);
        var token = targetMonitor.Token;
        targetMonitor.BeginRelocation(plan);
        ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: targetMonitor.Report);
        Assert.AreEqual(token, targetMonitor.Token);
        Assert.AreEqual("import", targetMonitor.Read()!.Operation);
        Assert.AreEqual(_current, targetMonitor.Read()!.BackupPath);
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_anchor));
        Assert.AreEqual("imported", Value(_target));
    }

    private ServerAppDataRelocation.Journal Queue() =>
        ServerAppDataRelocation.QueueImport(_anchor, _current, _target, _source, "/original/library");

    private void Save(ServerAppDataRelocation.Journal plan) =>
        File.WriteAllText(Path.Combine(_anchor, ServerAppDataRelocation.MarkerName), JsonConvert.SerializeObject(plan));

    private ImportProgressStore Monitor(ServerAppDataRelocation.Journal plan)
    {
        var result = new ImportProgressStore(_current);
        result.BeginRelocation(plan);
        return result;
    }

    private void StopAfterInnerImport(DataDirectoryLock currentLock, DataDirectoryLock targetLock, ImportProgressStore monitor)
    {
        Assert.ThrowsException<SimulatedCrash>(() => ServerAppDataRelocation.ApplyPending(_anchor, currentLock, targetLock, report: update =>
        {
            monitor.Report(update);
            if (update.Phase == "starting") throw new SimulatedCrash();
        }));
        Assert.AreEqual("importing", ServerAppDataRelocation.ReadPending(_anchor)!.Phase);
        Assert.AreEqual(_current, AnchorRedirect.TryRead(_anchor));
    }

    private static void CreateLibrary(string root, string value)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "app.json"), "{\"App\":{\"version\":\"1.0.0\"}}");
        File.WriteAllText(Path.Combine(root, "sentinel.txt"), value);
        using var connection = new SqliteConnection($"Data Source={Path.Combine(root, "bakabase_insideworld.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Sample(Value TEXT); INSERT INTO Sample VALUES($value)";
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static string Value(string root)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(root, "bakabase_insideworld.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Sample";
        return (string)command.ExecuteScalar()!;
    }

    private static string LibraryBytes(string root) => string.Join("\n", Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(root, path).StartsWith('.'))
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));

    private static DataDirectoryLock Lock(string path)
    {
        var attempt = DataDirectoryLock.TryAcquire(path);
        Assert.IsTrue(attempt.Acquired, attempt.Error?.Message);
        return attempt.Lock!;
    }

    private sealed class SimulatedCrash : Exception { }
}
