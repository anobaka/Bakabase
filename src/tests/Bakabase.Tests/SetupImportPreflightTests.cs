using System.Text.Json;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Service.Components.ServerData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class SetupImportPreflightTests
{
    private string _root = null!, _source = null!, _target = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = ServerSetupSession.Canonical(Path.Combine(Path.GetTempPath(), "bakabase-preflight-" + Guid.NewGuid().ToString("N")));
        _source = Path.Combine(_root, "source"); _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_source); Directory.CreateDirectory(_target);
        File.WriteAllText(Path.Combine(_source, "app.json"), JsonSerializer.Serialize(new { App = new { Version = ServerAppDataImport.RunningVersion.ToString() } }));
        Sql(_source, "CREATE TABLE Refs(Id INTEGER PRIMARY KEY, Path TEXT); INSERT INTO Refs VALUES(1,'Y:/Library/a.mkv'),(2,'Y:/Library/b.mkv'),(3,'Z:/Other/c.mkv');");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void DraftPersistsPartialEditsPrivatelyAndNeverRestoresCapabilitiesOrACommittedSelection()
    {
        using var held = Lock(_target);
        using (var session = new ServerSetupSession(_target, _target, false, held))
        {
            var request = Request(); request.PathPreflightId = "not-persisted"; request.PathPreviewId = "nor-this";
            var draft = session.SaveDraft(new(request, [new("Y:/", "")]), session.Token).Draft!;
            Assert.IsNotNull(draft);
            Assert.IsNull(draft.Request.PathPreviewId);
            Assert.IsTrue(session.Validate(new() { Operation = "initialize" }, session.Token).Valid);
            Assert.ThrowsException<UnauthorizedAccessException>(() => session.ReadDraft("wrong"));
        }
        using (var reopened = new ServerSetupSession(_target, _target, false, held))
        {
            Assert.IsFalse(reopened.Submitted);
            Assert.AreEqual("", reopened.ReadDraft(reopened.Token).Draft!.Rules.Single().TargetPrefix);
            Assert.AreEqual("idle", reopened.ReadPreflight(reopened.Token).Phase);
            var text = File.ReadAllText(Path.Combine(_target, SetupImportDraftStore.FileName));
            Assert.IsFalse(text.Contains(reopened.Token));
            if (!OperatingSystem.IsWindows()) Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(_target, SetupImportDraftStore.FileName)));
            reopened.ClearDraft(reopened.Token);
            Assert.IsNull(reopened.ReadDraft(reopened.Token).Draft);
        }
    }

    [TestMethod]
    public async Task PreflightIsReadOnlyAndProvidesAnAggregatedTreeAndExplicitUnmappedCounts()
    {
        var fingerprint = SetupImportPreflight.Fingerprint(_source);
        using var held = Lock(_target);
        using var session = new ServerSetupSession(_target, _target, false, held);
        var request = Request();
        var status = await Ready(session, request);
        Assert.AreEqual(3, status.UniquePaths);
        Assert.AreEqual(3L, status.ReferenceCount);
        var roots = session.ReadPathTree(status.Id!, null, 0, session.Token);
        Assert.AreEqual(2, roots.Total);
        Assert.AreEqual(2L, roots.Nodes.Single(node => node.Path == "Y:/").ReferenceCount);
        var children = session.ReadPathTree(status.Id!, "Y:/", 0, session.Token);
        Assert.AreEqual("Y:/Library", children.Nodes.Single().Path);
        Assert.IsTrue(children.Nodes.Single().HasChildren);
        var preview = session.PreviewPaths(new(status.Id!, [new("Y:/", "/Volumes/media")]), session.Token);
        Assert.AreEqual(2L, preview.MatchedReferences);
        Assert.AreEqual(1L, preview.UnmappedReferences);
        Assert.AreEqual("/Volumes/media/Library/a.mkv", preview.Examples[0].TargetPath);
        Assert.AreEqual(fingerprint, SetupImportPreflight.Fingerprint(_source));
        Assert.AreEqual(status.Id, session.StartPreflight(request, session.Token).Id);
    }

    [TestMethod]
    public async Task ChangedMappingOrSelectionCannotUseAPreviousPreview()
    {
        using var held = Lock(_target);
        using var session = new ServerSetupSession(_target, _target, false, held);
        var request = Request();
        var ready = await Ready(session, request);
        var rules = new[] { new PathMappingRule("Y:/", "/media") };
        var preview = session.PreviewPaths(new(ready.Id!, rules), session.Token);
        request.PathPreflightId = ready.Id; request.PathPreviewId = preview.PreviewId; request.PathMappings = rules;
        Assert.IsTrue(session.Validate(request, session.Token).Valid);
        request.PathMappings = [new("Y:/", "/different")];
        Assert.IsFalse(session.Validate(request, session.Token).Valid);
        Assert.ThrowsException<IOException>(() => session.Commit(request, session.Token));
        request.PathMappings = rules; request.TargetPath = Path.Combine(_root, "elsewhere");
        Assert.IsFalse(session.Validate(request, session.Token).Valid);
        Assert.IsNull(ServerAppDataImport.ReadPending(_target));
    }

    [TestMethod]
    public async Task LazyTreePagesRespectWindowsCasePosixCaseUncRootsAndPrefixBoundaries()
    {
        Sql(_source, "INSERT INTO Refs(Path) VALUES('y:/LIBRARY/c.mkv'),('Y:/LibraryElse/not-child.mkv'),('/Case/a'),('/case/b'),('//nas/share/folder/a');");
        for (var i = 0; i < 225; i++) Sql(_source, $"INSERT INTO Refs(Path) VALUES('Y:/Paged/file-{i:D3}.mkv');");
        using var held = Lock(_target);
        using var session = new ServerSetupSession(_target, _target, false, held);
        var scan = await Ready(session, Request());
        var roots = session.ReadPathTree(scan.Id!, null, 0, session.Token);
        Assert.IsTrue(roots.Nodes.Any(node => node.Path == "//nas/share"));
        var posix = session.ReadPathTree(scan.Id!, "/", 0, session.Token);
        CollectionAssert.AreEquivalent(new[] { "Case", "case" }, posix.Nodes.Select(node => node.Name).ToArray());
        var library = session.ReadPathTree(scan.Id!, "y:/library", 0, session.Token);
        Assert.AreEqual(3, library.Total);
        Assert.IsFalse(library.Nodes.Any(node => node.Name == "not-child.mkv"));
        var first = session.ReadPathTree(scan.Id!, "Y:/Paged", 0, session.Token);
        var last = session.ReadPathTree(scan.Id!, "Y:/Paged", 200, session.Token);
        Assert.AreEqual(225, first.Total); Assert.AreEqual(200, first.Nodes.Length); Assert.AreEqual(25, last.Nodes.Length);
        Assert.AreEqual(0, first.Nodes.Select(node => node.Path).Intersect(last.Nodes.Select(node => node.Path)).Count());
    }

    [TestMethod]
    public async Task SourceChangesRequireANewScanBeforeCommitAndForceScanKeepsTheDraft()
    {
        using var held = Lock(_target);
        using var session = new ServerSetupSession(_target, _target, false, held);
        var request = Request(); session.SaveDraft(new(request, [new("Y:/", "/media")]), session.Token);
        var scan = await Ready(session, request);
        var preview = session.PreviewPaths(new(scan.Id!), session.Token);
        request.PathPreflightId = scan.Id; request.PathPreviewId = preview.PreviewId;
        Sql(_source, "INSERT INTO Refs VALUES(8,'Y:/added.mkv');");
        Assert.IsFalse(session.Validate(request, session.Token).Valid);
        Assert.ThrowsException<IOException>(() => session.Commit(request, session.Token));
        var restarted = session.StartPreflight(request, session.Token, force: true);
        Assert.AreNotEqual(scan.Id, restarted.Id);
        await Ready(session, request);
        Assert.AreEqual(1, session.ReadDraft(session.Token).Draft!.Rules.Length);
        Assert.IsFalse(session.Validate(request, session.Token).Valid);
    }

    [TestMethod]
    public async Task ZeroMappingsCanBeReviewedAndDurablyQueuedAndTheDraftNeverBecomesImportedData()
    {
        using var held = Lock(_target);
        using var session = new ServerSetupSession(_target, _target, true, held);
        var request = Request();
        session.SaveDraft(new(request), session.Token);
        var ready = await Ready(session, request);
        var preview = session.PreviewPaths(new(ready.Id!), session.Token);
        Assert.AreEqual(3L, preview.UnmappedReferences);
        request.PathPreflightId = ready.Id; request.PathPreviewId = preview.PreviewId;
        session.Commit(request, session.Token);
        var journal = ServerAppDataImport.ReadPending(_target)!;
        Assert.AreEqual(2, journal.SchemaVersion);
        Assert.AreEqual(0, journal.PathPlan!.Rules.Length);
        ServerAppDataImport.ApplyPending(_target);
        Assert.AreEqual("Y:/Library/a.mkv", Value(_target));
        Assert.IsTrue(File.Exists(Path.Combine(_target, SetupImportDraftStore.FileName)));
        Assert.IsFalse(File.Exists(Path.Combine(_target, ServerAppDataImport.BackupsName, journal.Id, SetupImportDraftStore.FileName)));
        SetupImportDraftStore.Clear(_target, journal.PathPlan.DraftId);
        Assert.IsFalse(File.Exists(Path.Combine(_target, SetupImportDraftStore.FileName)));
    }

    [TestMethod]
    public void ASourceChangeAfterReviewFailsBeforeReplacingAnyExistingData()
    {
        File.WriteAllText(Path.Combine(_target, "keep.txt"), "existing data");
        var plan = new ImportPathPlan(SetupImportPreflight.Fingerprint(_source), [new("Y:/", "/media")]);
        using var held = Lock(_target);
        ServerAppDataImport.Queue(_source, _target, pathPlan: plan);
        Sql(_source, "INSERT INTO Refs VALUES(4,'Y:/later.mkv');");
        var error = Assert.ThrowsException<IOException>(() => ServerAppDataImport.ApplyPending(_target));
        StringAssert.Contains(error.Message, "changed after preflight");
        Assert.AreEqual("existing data", File.ReadAllText(Path.Combine(_target, "keep.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(_target, "bakabase_insideworld.db")));
        Assert.AreEqual("queued", ServerAppDataImport.ReadPending(_target)!.Phase);
    }

    [TestMethod]
    public void MappingWritesOnlyTheStageAndAnInterruptedMappingRestartsFromTheOriginalCopy()
    {
        File.WriteAllText(Path.Combine(_target, "keep.txt"), "existing data");
        var before = SetupImportPreflight.Fingerprint(_source);
        var rules = new[] { new PathMappingRule("Y:/", "/media") };
        using var held = Lock(_target);
        ServerAppDataImport.Queue(_source, _target, pathPlan: new(before, rules));
        var journal = ServerAppDataImport.ReadPending(_target)!;
        var mappingBegan = false;
        Assert.ThrowsException<OperationCanceledException>(() => ServerAppDataImport.ApplyPending(_target, report: progress =>
        {
            if (progress.Phase == "mapping") mappingBegan = true;
            if (mappingBegan && progress.Phase == "verifying") throw new OperationCanceledException();
        }));
        Assert.IsTrue(mappingBegan);
        Assert.AreEqual("existing data", File.ReadAllText(Path.Combine(_target, "keep.txt")));
        Assert.AreEqual(before, SetupImportPreflight.Fingerprint(_source));
        ServerAppDataImport.ApplyPending(_target);
        Assert.AreEqual("/media/Library/a.mkv", Value(_target));
        Assert.AreEqual(before, SetupImportPreflight.Fingerprint(_source));
        Assert.AreEqual("existing data", File.ReadAllText(Path.Combine(_target, ServerAppDataImport.BackupsName, journal.Id, "keep.txt")));
        Assert.IsNull(ServerAppDataImport.ReadPending(_target));
    }

    [TestMethod]
    public void CombinedImportPersistsAndVerifiesTheSameMappingPlanAcrossBothJournals()
    {
        var anchor = Path.Combine(_root, "anchor"); var next = Path.Combine(_root, "next");
        Directory.CreateDirectory(anchor);
        File.Copy(Path.Combine(_source, "app.json"), Path.Combine(_target, "app.json"));
        Sql(_target, "CREATE TABLE CurrentData(Id INTEGER PRIMARY KEY);");
        AnchorRedirect.Write(anchor, _target);
        var plan = new ImportPathPlan(SetupImportPreflight.Fingerprint(_source), [new("Y:/", "/media")]);
        using var currentLock = Lock(_target);
        var journal = ServerAppDataRelocation.QueueImport(anchor, _target, next, _source, pathPlan: plan);
        Assert.AreEqual(3, journal.SchemaVersion);
        using var monitor = new ImportProgressStore(_target);
        monitor.EnsureRelocation(journal);
        using var nextLock = Lock(next);
        ServerAppDataRelocation.ApplyPending(anchor, currentLock, nextLock);
        Assert.AreEqual(next, AnchorRedirect.TryRead(anchor));
        Assert.AreEqual("/media/Library/a.mkv", Value(next));
        var receipt = ServerAppDataImport.ReadCompletedReceipt(next, journal.Id, _source, null, plan);
        Assert.IsNotNull(receipt);
        Assert.ThrowsException<IOException>(() => ServerAppDataImport.ReadCompletedReceipt(next, journal.Id, _source, null,
            plan with { Rules = [new("Y:/", "/different")] }));
    }

    [TestMethod]
    public void CorruptDraftCanBeClearedAndAnUnrelatedDraftIsNotDeletedByOldCompletion()
    {
        File.WriteAllText(Path.Combine(_target, SetupImportDraftStore.FileName), "{truncated");
        Assert.IsNotNull(SetupImportDraftStore.Read(_target).Error);
        SetupImportDraftStore.Clear(_target);
        var saved = SetupImportDraftStore.Save(_target, new(Request())).Draft!;
        SetupImportDraftStore.Clear(_target, Guid.NewGuid().ToString("N"));
        Assert.AreEqual(saved.Id, SetupImportDraftStore.Read(_target).Draft!.Id);
        SetupImportDraftStore.Clear(_target, saved.Id);
        Assert.IsNull(SetupImportDraftStore.Read(_target).Draft);
    }

    [TestMethod]
    public void DraftSymlinksAreNeverFollowedOrDeleted()
    {
        if (OperatingSystem.IsWindows()) return;
        var outside = Path.Combine(_root, "outside.json"); File.WriteAllText(outside, "untouched");
        File.CreateSymbolicLink(Path.Combine(_target, SetupImportDraftStore.FileName), outside);
        Assert.IsNotNull(SetupImportDraftStore.Read(_target).Error);
        Assert.ThrowsException<IOException>(() => SetupImportDraftStore.Save(_target, new(Request())));
        Assert.ThrowsException<IOException>(() => SetupImportDraftStore.Clear(_target));
        Assert.AreEqual("untouched", File.ReadAllText(outside));
    }

    private ServerSetupSession.SetupRequest Request() => new() { Operation = "import", SourcePath = _source, TargetPath = _target };
    private static async Task<SetupPreflightStatus> Ready(ServerSetupSession session, ServerSetupSession.SetupRequest request)
    {
        var state = session.StartPreflight(request, session.Token);
        for (var i = 0; i < 500 && state.Phase == "scanning"; i++) { await Task.Delay(10); state = session.ReadPreflight(session.Token); }
        Assert.AreEqual("ready", state.Phase, state.Error); return state;
    }
    private static DataDirectoryLock Lock(string directory)
    {
        var result = DataDirectoryLock.TryAcquire(directory); Assert.IsTrue(result.Acquired, result.Error?.Message); return result.Lock!;
    }
    private static void Sql(string directory, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "bakabase_insideworld.db")};Pooling=False");
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    private static string Value(string directory)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "bakabase_insideworld.db")};Pooling=False");
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT Path FROM Refs WHERE Id=1;";
        return (string)command.ExecuteScalar()!;
    }
}
