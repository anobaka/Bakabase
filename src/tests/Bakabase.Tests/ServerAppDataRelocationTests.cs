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
public class ServerAppDataRelocationTests
{
    private string _root = null!;
    private string _source = null!;
    private string _target = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = ServerSetupSession.Canonical(Path.Combine(Path.GetTempPath(), "bakabase-relocation-tests-" + Guid.NewGuid().ToString("N")));
        _source = Path.Combine(_root, "source");
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_source);
        File.WriteAllText(Path.Combine(_source, "app.json"), "{\"App\":{\"Version\":\"1.0.0\",\"Language\":\"en\"}}");
        using (var connection = OpenDb(_source))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Sample(Id INTEGER PRIMARY KEY, Value TEXT); INSERT INTO Sample(Value) VALUES('source library');";
            command.ExecuteNonQuery();
        }
        Directory.CreateDirectory(Path.Combine(_source, "backups", "old-version"));
        File.WriteAllText(Path.Combine(_source, "backups", "old-version", "snapshot.txt"), "existing backup");
        Directory.CreateDirectory(Path.Combine(_source, "logs"));
        File.WriteAllText(Path.Combine(_source, "logs", "old.log"), "retained log");
        Directory.CreateDirectory(Path.Combine(_source, "empty-folder"));
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void RelocationPreservesTheWholeLibraryAndSourceThenSwitchesTheAnchor()
    {
        using var sourceLock = Lock(_source);
        var before = LibraryBytes();
        var journal = ServerAppDataRelocation.Queue(_source, _source, _target);
        using var targetLock = Lock(_target);
        using var monitor = Monitor(journal);
        var phases = new List<string>();
        var destination = ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock, report: update =>
        {
            phases.Add(update.Phase);
            monitor.Report(update);
        });
        Assert.AreEqual(_target, destination);
        Assert.AreEqual(_target, AnchorRedirect.TryRead(_source));
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_source));
        Assert.AreEqual(before, LibraryBytes());
        Assert.AreEqual("existing backup", File.ReadAllText(Path.Combine(_target, "backups", "old-version", "snapshot.txt")));
        Assert.AreEqual("retained log", File.ReadAllText(Path.Combine(_target, "logs", "old.log")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_target, "empty-folder")));
        Assert.IsTrue(ImportedAppDataRoots.Read(_target).Contains(_source));
        using var targetMonitor = new ImportProgressStore(_target);
        Assert.AreEqual(monitor.Token, targetMonitor.Token);
        Assert.AreEqual("relocate", targetMonitor.Read()!.Operation);
        Assert.AreEqual("starting", targetMonitor.Read()!.Phase);
        Assert.IsNull(targetMonitor.Read()!.BackupPath);
        CollectionAssert.IsSubsetOf(new[] { "scanning", "copying", "verifying", "installing", "starting" }, phases);
        AssertDb(_target);
    }

    [TestMethod]
    public void NonEmptyAndNestedTargetsAreRejectedWithoutCreatingAPlan()
    {
        Directory.CreateDirectory(_target);
        File.WriteAllText(Path.Combine(_target, "keep.txt"), "keep");
        Assert.IsFalse(ServerAppDataRelocation.Validate(_source, _source, _target).Valid);
        Assert.IsFalse(ServerAppDataRelocation.Validate(_source, _source, Path.Combine(_source, "child")).Valid);
        Assert.IsFalse(ServerAppDataRelocation.Validate(_source, _source, _root).Valid);
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_source));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(_target, "keep.txt")));
    }

    [TestMethod]
    public void CaseAliasedNestedTargetsCannotBeQueuedOnMacOrWindows()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
            Assert.Inconclusive("Case-insensitive overlap protection is specific to macOS and Windows.");
        var nested = Path.Combine(_root, "SOURCE", "new-library");
        var validation = ServerAppDataRelocation.Validate(_source, _source, nested);
        Assert.IsFalse(validation.Valid);
        StringAssert.Contains(validation.Error!, "non-nested");
        Assert.ThrowsException<IOException>(() => ServerAppDataRelocation.Queue(_source, _source, nested));
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_source));
        Assert.IsFalse(Directory.Exists(nested));
    }

    [TestMethod]
    public void PreviouslyQueuedCaseAliasedNestedTargetsAreRejectedBeforeRecovery()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
            Assert.Inconclusive("Case-insensitive overlap protection is specific to macOS and Windows.");
        var nested = Path.Combine(_root, "SOURCE", "new-library");
        var journal = new ServerAppDataRelocation.Journal { SourcePath = _source, TargetPath = nested };
        File.WriteAllText(Path.Combine(_source, ServerAppDataRelocation.MarkerName), JsonConvert.SerializeObject(journal));
        Assert.ThrowsException<IOException>(() => ServerAppDataRelocation.ReadPending(_source));
        Assert.IsFalse(Directory.Exists(nested));
        Assert.IsNull(AnchorRedirect.TryRead(_source));
    }

    [TestMethod]
    public void ExecutionRequiresBothCorrectLiveLocks()
    {
        using var sourceLock = Lock(_source);
        var journal = ServerAppDataRelocation.Queue(_source, _source, _target);
        using var targetLock = Lock(_target);
        using var monitor = Monitor(journal);
        Assert.ThrowsException<IOException>(() => ServerAppDataRelocation.ApplyPending(_source, targetLock, targetLock, report: monitor.Report));
        targetLock.Dispose();
        Assert.ThrowsException<IOException>(() => ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock, report: monitor.Report));
        Assert.AreEqual("queued", ServerAppDataRelocation.ReadPending(_source)!.Phase);
        Assert.IsNull(AnchorRedirect.TryRead(_source));
    }

    [TestMethod]
    public void UnstartedPlansCanBeCancelledWithoutTouchingTheDestination()
    {
        ServerAppDataRelocation.Queue(_source, _source, _target);
        ServerAppDataRelocation.Cancel(_source);
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_source));
        Assert.IsFalse(Directory.Exists(_target));
    }

    [TestMethod]
    public void InterruptedCopyRestartsAndCannotBeCancelledAsAnUnstartedPlan()
    {
        File.WriteAllBytes(Path.Combine(_source, "large.bin"), new byte[3 * 1024 * 1024]);
        var before = LibraryBytes();
        using var sourceLock = Lock(_source);
        var journal = ServerAppDataRelocation.Queue(_source, _source, _target);
        using var targetLock = Lock(_target);
        using var monitor = Monitor(journal);
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsException<OperationCanceledException>(() => ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock,
            report: update =>
            {
                monitor.Report(update);
                if (update.Phase == "copying" && update.CompletedBytes >= 1024 * 1024) cancellation.Cancel();
            }, cancellationToken: cancellation.Token));
        Assert.AreEqual("copying", ServerAppDataRelocation.ReadPending(_source)!.Phase);
        Assert.ThrowsException<IOException>(() => ServerAppDataRelocation.Cancel(_source));
        Assert.IsNull(AnchorRedirect.TryRead(_source));
        Assert.AreEqual(before, LibraryBytes());
        ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock, report: monitor.Report);
        Assert.AreEqual(3 * 1024 * 1024, new FileInfo(Path.Combine(_target, "large.bin")).Length);
        AssertDb(_target);
    }

    [TestMethod]
    public void PartialRootInstallationResumesWithoutMergingOrDeletingTheSource()
    {
        using var sourceLock = Lock(_source);
        var journal = ServerAppDataRelocation.Queue(_source, _source, _target);
        using var targetLock = Lock(_target);
        using var monitor = Monitor(journal);
        var before = LibraryBytes();
        Assert.ThrowsException<SimulatedCrash>(() => ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock, report: update =>
        {
            monitor.Report(update);
            if (update.Phase == "installing" && update.CompletedEntries == 1) throw new SimulatedCrash();
        }));
        Assert.AreEqual("installing", ServerAppDataRelocation.ReadPending(_source)!.Phase);
        Assert.IsNull(AnchorRedirect.TryRead(_source));
        ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock, report: monitor.Report);
        Assert.AreEqual(before, LibraryBytes());
        AssertDb(_target);
    }

    [TestMethod]
    public void RedirectFailureKeepsAReadyPlanAndTheOriginalLibraryAvailable()
    {
        using var sourceLock = Lock(_source);
        var journal = ServerAppDataRelocation.Queue(_source, _source, _target);
        using var targetLock = Lock(_target);
        using var monitor = Monitor(journal);
        Directory.CreateDirectory(Path.Combine(_source, AnchorRedirect.FileName + ".tmp"));
        try
        {
            ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock, report: monitor.Report);
            Assert.Fail("Expected a redirect write failure.");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Assert.AreEqual("ready", ServerAppDataRelocation.ReadPending(_source)!.Phase);
        Assert.IsNull(AnchorRedirect.TryRead(_source));
        AssertDb(_source);
        Directory.Delete(Path.Combine(_source, AnchorRedirect.FileName + ".tmp"));
        ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock, report: monitor.Report);
        Assert.AreEqual(_target, AnchorRedirect.TryRead(_source));
        AssertDb(_target);
    }

    [TestMethod]
    public void ACommittedRedirectCanFinishJournalCleanupUsingTheTargetMonitor()
    {
        using var sourceLock = Lock(_source);
        var journal = ServerAppDataRelocation.Queue(_source, _source, _target);
        using var targetLock = Lock(_target);
        using var sourceMonitor = Monitor(journal);
        ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock, report: sourceMonitor.Report);
        journal.Phase = "ready";
        journal.IncomingEntries = Directory.EnumerateFileSystemEntries(_target)
            .Select(Path.GetFileName).Where(p => p != DataDirectoryLock.FileName && p != ImportProgressStore.FileName).ToArray()!;
        File.WriteAllText(Path.Combine(_source, ServerAppDataRelocation.MarkerName), JsonConvert.SerializeObject(journal));
        sourceMonitor.Dispose();
        using var targetMonitor = new ImportProgressStore(_target);
        var token = targetMonitor.Token;
        targetMonitor.BeginRelocation(journal);
        ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock, report: targetMonitor.Report);
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_source));
        Assert.IsTrue(targetMonitor.Authorize(token));
        Assert.AreEqual("starting", targetMonitor.Read()!.Phase);
        AssertDb(_target);
    }

    [TestMethod]
    public void CorruptCopiedDatabaseNeverSwitchesTheAnchor()
    {
        File.WriteAllText(Path.Combine(_source, "bakabase_insideworld.db"), "invalid sqlite");
        using var sourceLock = Lock(_source);
        var journal = ServerAppDataRelocation.Queue(_source, _source, _target);
        using var targetLock = Lock(_target);
        using var monitor = Monitor(journal);
        Assert.ThrowsException<SqliteException>(() => ServerAppDataRelocation.ApplyPending(_source, sourceLock, targetLock, report: monitor.Report));
        Assert.IsNull(AnchorRedirect.TryRead(_source));
        Assert.IsFalse(File.Exists(Path.Combine(_target, "bakabase_insideworld.db")));
    }

    private ImportProgressStore Monitor(ServerAppDataRelocation.Journal journal)
    {
        var result = new ImportProgressStore(_source);
        result.BeginRelocation(journal);
        return result;
    }

    private string LibraryBytes() => string.Join("\n", Directory.EnumerateFiles(_source, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(_source, path).StartsWith('.'))
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(_source, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));

    private static DataDirectoryLock Lock(string directory)
    {
        var result = DataDirectoryLock.TryAcquire(directory);
        Assert.IsTrue(result.Acquired, result.Error?.Message);
        return result.Lock!;
    }

    private static SqliteConnection OpenDb(string directory)
    {
        var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "bakabase_insideworld.db")};Pooling=False");
        connection.Open();
        return connection;
    }

    private static void AssertDb(string directory)
    {
        using var connection = OpenDb(directory);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Sample WHERE Id=1";
        Assert.AreEqual("source library", command.ExecuteScalar());
    }

    private sealed class SimulatedCrash : Exception { }
}
