using System.Text.Json;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Service.Components.ServerData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ServerSetupSessionTests
{
    private string _root = null!;
    private string _anchor = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-setup-tests-" + Guid.NewGuid().ToString("N"));
        _root = ServerSetupSession.Canonical(_root);
        _anchor = Path.Combine(_root, "anchor");
        Directory.CreateDirectory(_anchor);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void AFirstRunPersistsAPrivateTokenWithoutCreatingApplicationData()
    {
        using var dataLock = Lock(_anchor);
        string token;
        using (var session = new ServerSetupSession(_anchor, _anchor, false, dataLock))
        {
            token = session.Token;
            Assert.IsTrue(ServerSetupSession.RequiresSetup(_anchor, _anchor));
            Assert.AreEqual("first-run", session.Mode);
            Assert.IsTrue(session.Read().CanChooseTargetPath);
            Assert.IsFalse(session.Authorize(null));
            Assert.IsFalse(session.Authorize(new string('x', 64)));
            Assert.IsTrue(session.Authorize(token));
            Assert.IsFalse(File.Exists(Path.Combine(_anchor, "app.json")));
            Assert.IsFalse(Directory.EnumerateFiles(_anchor, "*.db", SearchOption.AllDirectories).Any());
        }
        using var resumed = new ServerSetupSession(_anchor, _anchor, false, dataLock);
        Assert.AreEqual(token, resumed.Token);
        if (!OperatingSystem.IsWindows())
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(_anchor, ServerSetupSession.FileName)));
    }

    [TestMethod]
    public void EmptyLibraryStartsOnlyAfterAnAuthorizedExplicitSubmissionAndResponseCompletion()
    {
        using var dataLock = Lock(_anchor);
        using var session = new ServerSetupSession(_anchor, _anchor, true, dataLock);
        var request = new ServerSetupSession.SetupRequest();
        Assert.ThrowsException<UnauthorizedAccessException>(() => session.Commit(request, "wrong"));
        Assert.IsTrue(ServerSetupSession.RequiresSetup(_anchor, _anchor));
        var token = session.Token;
        var result = session.Commit(request, token);
        Assert.IsFalse(result.RequiresRestart);
        Assert.AreEqual("starting", result.Progress!.Phase);
        Assert.AreEqual("initialize", result.Progress.Operation);
        Assert.IsNull(result.Progress.BackupPath);
        Assert.IsFalse(session.Completion.IsCompleted);
        Assert.IsFalse(session.Authorize(token));
        Assert.ThrowsException<UnauthorizedAccessException>(() => session.Commit(request, token));
        Assert.IsFalse(ServerSetupSession.RequiresSetup(_anchor, _anchor));
        Assert.IsFalse(File.Exists(Path.Combine(_anchor, ServerAppDataImport.MarkerName)));
        Assert.IsFalse(File.Exists(Path.Combine(_anchor, "bakabase_insideworld.db")));
        session.SignalCommitted();
        Assert.IsTrue(session.Completion.IsCompletedSuccessfully);
        Assert.AreSame(dataLock, session.Completion.Result.DataLock);
        session.Monitoring!.Dispose(); // Ownership transfers with the completed HTTP response.
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void DeploymentFixedOrContainerTargetsCannotBeChanged(bool fixedPath, bool docker)
    {
        using var dataLock = Lock(_anchor);
        using var session = new ServerSetupSession(_anchor, _anchor, fixedPath, dataLock, docker);
        var other = Path.Combine(_root, "other");
        var validation = session.Validate(new() { TargetPath = other }, session.Token);
        Assert.IsFalse(session.Read().CanChooseTargetPath);
        Assert.IsFalse(validation.Valid);
        StringAssert.Contains(validation.Error!, "fixed");
        Assert.IsFalse(Directory.Exists(other));
        Assert.IsNull(AnchorRedirect.TryRead(_anchor));
    }

    [TestMethod]
    public void ChoosingANewNativeTargetPersistsTheRedirectAndTransfersAnAlreadyHeldLock()
    {
        using var anchorLock = Lock(_anchor);
        using var session = new ServerSetupSession(_anchor, _anchor, false, anchorLock);
        var target = Path.Combine(_root, "selected data");
        var response = session.Commit(new() { TargetPath = target }, session.Token);
        Assert.AreEqual(target, AnchorRedirect.TryRead(_anchor));
        Assert.IsFalse(ServerSetupSession.RequiresSetup(_anchor, target));
        Assert.IsFalse(DataDirectoryLock.TryAcquire(target).Acquired);
        Assert.IsTrue(session.Monitoring!.Authorize(response.MonitorToken));
        session.SignalCommitted();
        using var targetLock = session.Completion.Result.DataLock;
        session.Monitoring.Dispose();
        Assert.AreNotSame(anchorLock, targetLock);
        Assert.IsTrue(targetLock.IsHeld);
        Assert.IsTrue(anchorLock.IsHeld);
        Assert.IsFalse(File.Exists(Path.Combine(target, "app.json")));
    }

    [TestMethod]
    public void ABusyTargetOrOrdinaryFilesNeverGetOverwritten()
    {
        using var anchorLock = Lock(_anchor);
        using var session = new ServerSetupSession(_anchor, _anchor, false, anchorLock);
        var target = Path.Combine(_root, "busy");
        using var owner = Lock(target);
        Assert.ThrowsException<IOException>(() => session.Commit(new() { TargetPath = target }, session.Token));
        File.WriteAllText(Path.Combine(target, "family-photo.jpg"), "do not overwrite");
        var result = session.Validate(new() { TargetPath = target }, session.Token);
        Assert.IsFalse(result.Valid);
        Assert.AreEqual("do not overwrite", File.ReadAllText(Path.Combine(target, "family-photo.jpg")));
        Assert.IsNull(AnchorRedirect.TryRead(_anchor));
        Assert.IsTrue(ServerSetupSession.RequiresSetup(_anchor, _anchor));
    }

    [TestMethod]
    public void CaseSensitiveVolumesRequireASeparateLockForCaseDistinctTargets()
    {
        if (OperatingSystem.IsWindows()) return;
        var upper = Path.Combine(_root, "Library");
        var lower = Path.Combine(_root, "library");
        Directory.CreateDirectory(upper);
        if (Directory.Exists(lower)) return; // The current volume aliases these spellings.
        Directory.CreateDirectory(lower);
        using var owner = Lock(upper);
        using (var fixedSession = new ServerSetupSession(upper, upper, true, owner))
            Assert.IsFalse(fixedSession.Validate(new() { TargetPath = lower }, fixedSession.Token).Valid);
        using var session = new ServerSetupSession(upper, upper, false, owner);
        session.Commit(new() { TargetPath = lower }, session.Token);
        Assert.IsFalse(DataDirectoryLock.TryAcquire(lower).Acquired);
        session.SignalCommitted();
        using var selected = session.Completion.Result.DataLock;
        session.Monitoring!.Dispose();
        Assert.AreNotSame(owner, selected);
        Assert.AreEqual(lower, selected.Directory);
    }

    [TestMethod]
    public void FirstRunImportQueuesTheExistingImporterWithoutOpeningOrCopyingTheLibrary()
    {
        using var dataLock = Lock(_anchor);
        var source = Source();
        var before = File.ReadAllBytes(Path.Combine(source, "bakabase_insideworld.db"));
        using var session = new ServerSetupSession(_anchor, _anchor, true, dataLock);
        var result = session.Commit(new() { SourcePath = source, OriginalDataPath = "/old/AppData" }, session.Token);
        Assert.IsFalse(result.RequiresRestart);
        Assert.AreEqual("queued", result.Progress!.Phase);
        Assert.AreEqual(source, ServerAppDataImport.ReadPending(_anchor)!.SourcePath);
        Assert.IsFalse(File.Exists(Path.Combine(_anchor, "bakabase_insideworld.db")));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Path.Combine(source, "bakabase_insideworld.db")));
        Assert.IsTrue(session.Monitoring!.Authorize(result.MonitorToken));
        Assert.IsFalse(ServerSetupSession.RequiresSetup(_anchor, _anchor));
    }

    [TestMethod]
    public void ExistingServiceImportUsesTheSameRequestButRequiresAnExternalRestart()
    {
        using var dataLock = Lock(_anchor);
        using var monitoring = new ImportProgressStore(_anchor);
        using var session = ServerSetupSession.ForImport(_anchor, monitoring);
        var source = Source();
        Assert.AreEqual("import", session.Mode);
        CollectionAssert.AreEquivalent(new[] { "relocate", "import" }, session.Read().AllowedOperations);
        Assert.IsFalse(session.Validate(new(), session.Token).Valid);
        var result = session.Commit(new() { SourcePath = source }, session.Token);
        Assert.IsTrue(result.RequiresRestart);
        Assert.AreEqual("queued", result.Progress!.Phase);
        Assert.IsTrue(monitoring.Authorize(result.MonitorToken));
        Assert.IsFalse(session.Authorize(session.Token));
        session.SignalCommitted();
        Assert.IsFalse(session.Completion.IsCompleted);
        Assert.IsFalse(File.Exists(Path.Combine(_anchor, ServerSetupSession.FileName)));
    }

    [TestMethod]
    [DataRow("database")]
    [DataRow("wrapped-options")]
    [DataRow("legacy-options")]
    public void ExistingDeploymentsSkipFirstRunWithoutReadingTheirDatabase(string fixture)
    {
        if (fixture == "database") File.WriteAllText(Path.Combine(_anchor, "bakabase_insideworld.db"), "never opened");
        else File.WriteAllText(Path.Combine(_anchor, "app.json"), fixture == "wrapped-options"
            ? "\uFEFF{\"App\":{\"Version\":\"1.0.0\"}}" : "{\"version\":\"1.0.0\"}");
        Assert.IsFalse(ServerSetupSession.RequiresSetup(_anchor, _anchor));
        Assert.IsFalse(File.Exists(Path.Combine(_anchor, ServerSetupSession.FileName)));
    }

    [TestMethod]
    public void ACommittedRedirectCanRecoverAfterACrashButDoesNotOverrideLaterUserChoices()
    {
        var target = Path.Combine(_root, "selected");
        Directory.CreateDirectory(target);
        using (var monitoring = new ImportProgressStore(target))
        {
            monitoring.EnsureQueued(new ServerAppDataImport.Journal());
            monitoring.Starting();
        }
        File.WriteAllText(Path.Combine(_anchor, ServerSetupSession.FileName), JsonSerializer.Serialize(new
            { token = new string('A', 64), submitted = true, selectedTarget = target, redirectPending = true }));
        ServerSetupSession.RecoverSubmittedRedirect(_anchor, false);
        Assert.AreEqual(target, AnchorRedirect.TryRead(_anchor));
        Assert.IsFalse(ServerSetupSession.RequiresSetup(_anchor, target));
        var later = Path.Combine(_root, "later user choice");
        AnchorRedirect.Write(_anchor, later);
        ServerSetupSession.RecoverSubmittedRedirect(_anchor, false);
        Assert.AreEqual(later, AnchorRedirect.TryRead(_anchor));
    }

    [TestMethod]
    public void AMissingConfiguredVolumeDoesNotSilentlyBecomeANewEmptyLibrary()
    {
        File.WriteAllText(Path.Combine(_anchor, ServerSetupSession.FileName), JsonSerializer.Serialize(new
            { token = new string('A', 64), submitted = true, selectedTarget = _anchor, redirectPending = false }));
        Assert.ThrowsException<IOException>(() => ServerSetupSession.RequiresSetup(_anchor, _anchor));
        Assert.IsFalse(File.Exists(Path.Combine(_anchor, "bakabase_insideworld.db")));
    }

    [TestMethod]
    public void ProgressPersistenceFailureCancelsOnlyTheNewlyQueuedImport()
    {
        using var dataLock = Lock(_anchor);
        using var monitoring = new ImportProgressStore(_anchor);
        using var session = ServerSetupSession.ForImport(_anchor, monitoring);
        Directory.CreateDirectory(Path.Combine(_anchor, ImportProgressStore.FileName + ".tmp"));
        ExpectIoFailure(() => session.Commit(new() { SourcePath = Source() }, session.Token));
        Assert.IsNull(ServerAppDataImport.ReadPending(_anchor));
        Assert.IsFalse(session.Submitted);
        Assert.AreEqual("failed", monitoring.Read()!.Phase);
        Assert.IsFalse(File.Exists(Path.Combine(_anchor, "bakabase_insideworld.db")));
    }

    [TestMethod]
    public void SetupIntentPersistenceFailureCancelsTheImportAndReleasesTheUnselectedTarget()
    {
        using var anchorLock = Lock(_anchor);
        using var session = new ServerSetupSession(_anchor, _anchor, false, anchorLock);
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(_anchor, ServerSetupSession.FileName + ".tmp"));
        ExpectIoFailure(() => session.Commit(new() { TargetPath = target, SourcePath = Source() }, session.Token));
        Assert.IsNull(ServerAppDataImport.ReadPending(target));
        Assert.IsFalse(session.Submitted);
        Assert.IsNull(AnchorRedirect.TryRead(_anchor));
        using var released = Lock(target);
        Assert.IsTrue(released.IsHeld);
    }

    [TestMethod]
    public void RedirectFailureAfterDurableSubmissionRequiresRestartAndDoesNotStartTheApplication()
    {
        using var anchorLock = Lock(_anchor);
        var target = Path.Combine(_root, "target");
        using (var session = new ServerSetupSession(_anchor, _anchor, false, anchorLock))
        {
            Directory.CreateDirectory(Path.Combine(_anchor, AnchorRedirect.FileName + ".tmp"));
            var result = session.Commit(new() { TargetPath = target }, session.Token);
            Assert.IsTrue(result.RequiresRestart);
            Assert.AreEqual("failed", result.Progress!.Phase);
            StringAssert.Contains(result.Progress.Error!, "choices were saved");
            Assert.IsTrue(session.Submitted);
            session.SignalCommitted();
            Assert.IsFalse(session.Completion.IsCompleted);
            Assert.IsNull(AnchorRedirect.TryRead(_anchor));
            Assert.IsFalse(DataDirectoryLock.TryAcquire(target).Acquired);
            Assert.IsFalse(File.Exists(Path.Combine(target, "bakabase_insideworld.db")));
        }
        Directory.Delete(Path.Combine(_anchor, AnchorRedirect.FileName + ".tmp"));
        ServerSetupSession.RecoverSubmittedRedirect(_anchor, false);
        Assert.AreEqual(target, AnchorRedirect.TryRead(_anchor));
        Assert.IsFalse(ServerSetupSession.RequiresSetup(_anchor, target));
        using var released = Lock(target);
        Assert.IsTrue(released.IsHeld);
    }

    private static void ExpectIoFailure(Action action)
    {
        try { action(); Assert.Fail("Expected the simulated write failure."); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [TestMethod]
    public void RelocationQueuesTheCurrentLibraryWithoutAnImportSourceOrEarlyCopy()
    {
        var source = Source();
        var target = Path.Combine(_root, "new-location");
        using var monitor = new ImportProgressStore(source);
        using var session = ServerSetupSession.ForRelocation(_anchor, source, monitor);
        var request = new ServerSetupSession.SetupRequest { TargetPath = target };
        Assert.IsTrue(session.Read().CanChooseTargetPath);
        Assert.IsTrue(session.Validate(request, session.Token).Valid);
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_anchor));
        var result = session.Commit(request, session.Token);
        Assert.IsTrue(result.RequiresRestart);
        Assert.AreEqual("relocate", result.Progress!.Operation);
        Assert.AreEqual("queued", result.Progress.Phase);
        Assert.AreEqual(source, result.Progress.SourcePath);
        Assert.AreEqual(target, result.Progress.TargetPath);
        Assert.IsFalse(Directory.Exists(target));
        Assert.IsTrue(File.Exists(Path.Combine(source, "bakabase_insideworld.db")));
        Assert.IsNull(ServerAppDataImport.ReadPending(source));
        Assert.IsFalse(session.Authorize(session.Token));
    }

    [TestMethod]
    public void ExistingImportEntryCanChooseRelocationButCannotCreateAnEmptyLibrary()
    {
        var source = Source();
        var target = Path.Combine(_root, "move-from-shared-wizard");
        using var monitor = new ImportProgressStore(source);
        using var session = ServerSetupSession.ForImport(source, monitor, _anchor);
        Assert.IsFalse(session.Validate(new() { Operation = "initialize" }, session.Token).Valid);
        Assert.IsTrue(session.Authorize(session.Token));
        var result = session.Commit(new() { Operation = "relocate", TargetPath = target }, session.Token);
        Assert.AreEqual("relocate", result.Progress!.Operation);
        Assert.AreEqual(target, ServerAppDataRelocation.ReadPending(_anchor)!.TargetPath);
        Assert.IsNull(ServerAppDataImport.ReadPending(source));
    }

    [TestMethod]
    public void ExistingRelocationEntryCanChooseImportWithoutMovingItsDirectory()
    {
        var source = Source();
        var other = Path.Combine(_root, "other-instance");
        Directory.Move(source, other);
        source = Source();
        using var monitor = new ImportProgressStore(source);
        using var session = ServerSetupSession.ForRelocation(_anchor, source, monitor);
        var result = session.Commit(new() { Operation = "import", SourcePath = other }, session.Token);
        Assert.AreEqual("import", result.Progress!.Operation);
        Assert.AreEqual(other, ServerAppDataImport.ReadPending(source)!.SourcePath);
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_anchor));
    }

    [TestMethod]
    public void RelocationDoesNotAdoptForeignDataOrOverwriteANonemptyDestination()
    {
        var source = Source();
        using var monitor = new ImportProgressStore(source);
        using var session = ServerSetupSession.ForRelocation(_anchor, source, monitor);
        var target = Path.Combine(_root, "occupied");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        Assert.IsFalse(session.Validate(new() { TargetPath = target }, session.Token).Valid);
        Assert.IsFalse(session.Validate(new() { TargetPath = Path.Combine(_root, "empty"), SourcePath = source }, session.Token).Valid);
        Assert.IsFalse(session.Validate(new() { TargetPath = source }, session.Token).Valid);
        Assert.IsFalse(session.Validate(new() { TargetPath = Path.Combine(source, "child") }, session.Token).Valid);
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_anchor));
    }

    [TestMethod]
    public void RelocationRollsBackItsQueueIfProgressCannotBePersisted()
    {
        var source = Source();
        using var monitor = new ImportProgressStore(source);
        using var session = ServerSetupSession.ForRelocation(_anchor, source, monitor);
        Directory.CreateDirectory(Path.Combine(source, ImportProgressStore.FileName + ".tmp"));
        ExpectIoFailure(() => session.Commit(new() { TargetPath = Path.Combine(_root, "empty") }, session.Token));
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_anchor));
        Assert.IsFalse(session.Submitted);
        Assert.IsTrue(File.Exists(Path.Combine(source, "bakabase_insideworld.db")));
    }

    [TestMethod]
    public void ExistingImportCanQueueAnExternalLibraryIntoANewDirectoryWithoutTouchingTheTarget()
    {
        var current = Source();
        var external = Path.Combine(_root, "external");
        Directory.Move(current, external);
        current = Source();
        using var currentLock = Lock(current);
        using var monitor = new ImportProgressStore(current);
        using var session = ServerSetupSession.ForImport(current, monitor, _anchor);
        var target = Path.Combine(_root, "new-location");
        var request = new ServerSetupSession.SetupRequest { Operation = "import", SourcePath = external, TargetPath = target };
        var validation = session.Validate(request, session.Token);
        Assert.IsTrue(validation.Valid, validation.Error);
        Assert.AreEqual(external, validation.SourcePath);
        var result = session.Commit(request, session.Token);
        Assert.IsTrue(result.RequiresRestart);
        Assert.AreEqual("import", result.Progress!.Operation);
        Assert.AreEqual(external, result.Progress.SourcePath);
        Assert.AreEqual(current, result.Progress.BackupPath);
        Assert.AreEqual(target, result.Progress.TargetPath);
        var plan = ServerAppDataRelocation.ReadPending(_anchor)!;
        Assert.AreEqual(2, plan.SchemaVersion);
        Assert.AreEqual(current, plan.SourcePath);
        Assert.AreEqual(external, plan.ImportSourcePath);
        Assert.AreEqual(target, plan.TargetPath);
        Assert.IsFalse(Directory.Exists(target));
        Assert.IsNull(ServerAppDataImport.ReadPending(current));
        Assert.IsNull(AnchorRedirect.TryRead(_anchor));
        Assert.IsFalse(session.Authorize(session.Token));
        Assert.IsFalse(session.Completion.IsCompleted);
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(false)]
    [DataRow(true)]
    public void FixedDeploymentsCannotUseImportToBypassTheirTargetRestriction(bool container)
    {
        var current = Source();
        var external = Path.Combine(_root, "external");
        Directory.Move(current, external);
        current = Source();
        var env = container ? "DOTNET_RUNNING_IN_CONTAINER" : AppDataAnchor.Current.EnvVarName;
        var previous = Environment.GetEnvironmentVariable(env);
        try
        {
            Environment.SetEnvironmentVariable(env, container ? "true" : current);
            using var monitor = new ImportProgressStore(current);
            using var session = ServerSetupSession.ForImport(current, monitor, _anchor);
            var target = Path.Combine(_root, "fixed-target-bypass");
            var request = new ServerSetupSession.SetupRequest { Operation = "import", SourcePath = external, TargetPath = target };
            var validation = session.Validate(request, session.Token);
            Assert.IsFalse(validation.Valid);
            StringAssert.Contains(validation.Error!, "fixed");
            Assert.ThrowsException<IOException>(() => session.Commit(request, session.Token));
            Assert.IsNull(ServerAppDataRelocation.ReadPending(_anchor));
            Assert.IsFalse(Directory.Exists(target));
            Assert.IsTrue(session.Authorize(session.Token));
        }
        finally { Environment.SetEnvironmentVariable(env, previous); }
    }

    [TestMethod]
    public void CombinedImportCancelsOnlyItsOuterQueueWhenProgressCannotBePersisted()
    {
        var current = Source();
        var external = Path.Combine(_root, "external");
        Directory.Move(current, external);
        current = Source();
        using var monitor = new ImportProgressStore(current);
        using var session = ServerSetupSession.ForImport(current, monitor, _anchor);
        Directory.CreateDirectory(Path.Combine(current, ImportProgressStore.FileName + ".tmp"));
        var target = Path.Combine(_root, "new-location");
        ExpectIoFailure(() => session.Commit(new() { Operation = "import", SourcePath = external, TargetPath = target }, session.Token));
        Assert.IsNull(ServerAppDataRelocation.ReadPending(_anchor));
        Assert.IsFalse(Directory.Exists(target));
        Assert.IsNull(ServerAppDataImport.ReadPending(current));
        Assert.IsTrue(session.Authorize(session.Token));
    }

    private static DataDirectoryLock Lock(string directory)
    {
        var attempt = DataDirectoryLock.TryAcquire(directory);
        Assert.IsTrue(attempt.Acquired, attempt.Error?.Message);
        return attempt.Lock!;
    }

    private string Source()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "app.json"), JsonSerializer.Serialize(new
            { App = new { Version = ServerAppDataImport.RunningVersion.ToString() } }));
        using var connection = new SqliteConnection($"Data Source={Path.Combine(source, "bakabase_insideworld.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Example(Id INTEGER PRIMARY KEY);";
        command.ExecuteNonQuery();
        return source;
    }
}
