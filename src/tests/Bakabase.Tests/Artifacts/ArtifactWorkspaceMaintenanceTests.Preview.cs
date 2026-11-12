using System.Text.Json;
using System.Data.Common;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bakabase.Tests.Artifacts;

public sealed partial class ArtifactWorkspaceMaintenanceTests
{
    [TestMethod]
    public async Task Preview_reference_reads_are_reused_but_mutation_still_sees_new_references()
    {
        var fixture = await Fixture();
        var counter = new ReferenceReadCounter();
        await using var observed = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite(_db.Database.GetDbConnection().ConnectionString).AddInterceptors(counter).Options);
        var guard = new ArtifactCleanupReferenceGuard(observed, cachePreviewReads: true);
        Assert.IsFalse(await guard.HasReferencesAsync(fixture.Artifact, [], default, [fixture.Intermediate]));
        var firstReadCount = counter.Reads;
        Assert.IsTrue(firstReadCount >= 9);
        Assert.IsFalse(await guard.HasReferencesAsync(fixture.Artifact, [], default, [fixture.Intermediate]));
        Assert.AreEqual(firstReadCount, counter.Reads, "Candidates in one preview reuse full-table reference projections.");
        var preview = (await _maintenance.PreviewAsync([fixture.Artifact.Id], default)).Single();
        _db.PathMarks.Add(new PathMarkDbModel {Id = 999, Path = fixture.Intermediate, ConfigJson = "{}"});
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var execution = (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default,
            new Dictionary<string, string> {{fixture.Artifact.Id, preview.SelectionFingerprint!}})).Single();
        Assert.IsFalse(execution.Completed);
        Assert.AreEqual("ReferencedPath", execution.Reason);
        Assert.IsTrue(File.Exists(fixture.Intermediate));
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => guard.HasReferencesAsync(
            fixture.Artifact, [], cancelled.Token, [fixture.Intermediate]));
    }

    private sealed class ReferenceReadCounter : DbCommandInterceptor
    {
        internal int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [TestMethod]
    public async Task Preview_and_description_do_not_open_private_payload_or_repeat_the_selection_scan()
    {
        var fixture = await Fixture();
        ArtifactWorkspaceCleanupPreview preview;
        await using (var locked = new FileStream(fixture.Intermediate, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            preview = (await _maintenance.PreviewAsync([fixture.Artifact.Id], default)).Single();
            Assert.IsTrue(preview.Eligible, preview.Reason);
            var described = await _maintenance.DescribeFilesAsync(preview, default);
            Assert.AreEqual(fixture.Intermediate, described.Single().Path);
        }
        // A summary displays its fixed observation; mutation independently rejects stale files.
        File.Delete(fixture.Intermediate);
        Assert.AreEqual(1, (await _maintenance.DescribeFilesAsync(preview, default)).Count);
        var result = (await _maintenance.ReclaimAsync([fixture.Artifact.Id], default,
            new Dictionary<string, string> {{fixture.Artifact.Id, preview.SelectionFingerprint!}})).Single();
        Assert.IsFalse(result.Completed);
        Assert.AreEqual("SelectionChanged", result.Reason);
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Metadata_preview_requires_byte_checks_unless_explicitly_waived(bool skipDeliveryVerification)
    {
        var fixture = await Fixture();
        var timestamp = File.GetLastWriteTimeUtc(fixture.Intermediate);
        await File.WriteAllTextAsync(fixture.Intermediate, "tampered"); // Same length as "original".
        File.SetLastWriteTimeUtc(fixture.Intermediate, timestamp);
        ArtifactDeliveryCleanupAuthorization? authorization = null;
        if (skipDeliveryVerification)
        {
            var root = Directory.CreateDirectory(Path.Combine(_root, "delivered")).FullName;
            var file = Path.Combine(root, "final.bin");
            await File.WriteAllTextAsync(file, "accepted delivery");
            var output = await _store.AdoptAsync(new("publish", "publish", "files.publish", fixture.Artifact.Fingerprint),
                new(root, [file], "publishedFiles", true, [fixture.Artifact.Id]), "test");
            authorization = new(new("accepted", DateTimeOffset.UtcNow, 1, 1, 1, true, [1],
                [new(output.Id, output.OperationId, output.Fingerprint, output.RootDirectory)], true), _ => Task.FromResult(true), _store);
            File.Move(file, Path.Combine(_root, "accepted-file.bin"));
        }
        var preview = (authorization == null ? await _maintenance.PreviewAsync([fixture.Artifact.Id], default)
            : await _maintenance.PreviewForDeletionAsync([fixture.Artifact.Id], authorization, default)).Single();
        Assert.IsTrue(preview.Eligible, "The confirmation preview is a metadata observation, not a full content audit.");
        var selection = new Dictionary<string, string> {{fixture.Artifact.Id, preview.SelectionFingerprint!}};
        var result = (authorization == null ? await _maintenance.ReclaimAsync([fixture.Artifact.Id], default, selection)
            : await _maintenance.ReclaimForDeletionAsync([fixture.Artifact.Id], authorization, selection, default)).Single();
        Assert.AreEqual(skipDeliveryVerification, result.Completed, result.Reason);
        if (skipDeliveryVerification) Assert.IsFalse(File.Exists(fixture.Intermediate));
        else
        {
            Assert.AreEqual("SelectionChanged", result.Reason);
            Assert.AreEqual("tampered", await File.ReadAllTextAsync(fixture.Intermediate));
        }
        Assert.IsTrue(File.Exists(fixture.Original));
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
    }

    [TestMethod]
    public async Task Recursive_preview_does_not_open_nested_payloads_but_execution_rejects_changed_bytes()
    {
        var directory = Path.Combine(_root, "recursive");
        var archive = Path.Combine(_root, "two-files.zip");
        await File.WriteAllTextAsync(archive, "archive");
        var result = await new RecursiveArchiveExecutor(new TwoFileExtraction()).ExecuteAsync(
            _root, [archive], directory, new() {MinimumFreeBytes = 0});
        Assert.IsTrue(result.Completed, result.Message);
        var state = JsonSerializer.Deserialize<RecursiveArchiveExecutor.State>(await File.ReadAllTextAsync(
            Path.Combine(directory, "recursive-state.json")), Json)!;
        var nodeRoot = Path.Combine(directory, state.Nodes.Single().OutputRoot!);
        var nested = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(
            Path.Combine(Path.GetDirectoryName(nodeRoot)!, "state.json")), Json)!;
        var privatePaths = await RecursiveArchiveExecutor.GetReclaimableFilesAsync(directory);
        Assert.AreEqual(5, privatePaths.Count);
        var artifact = await _store.AdoptAsync(new("preview-recursive", "preview-recursive", "files.process", "fixture"),
            new(result.OutputDirectory!, result.Files, "processedFiles", true), "fixture");
        var streams = privatePaths.Select(path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)).ToArray();
        ArtifactWorkspaceCleanupPreview preview;
        try
        {
            preview = (await _maintenance.PreviewAsync([artifact.Id], default)).Single();
            Assert.IsTrue(preview.Eligible, preview.Reason);
            Assert.AreEqual(5, preview.FileCount);
        }
        finally { foreach (var stream in streams) await stream.DisposeAsync(); }
        var changed = nested.FinalFiles.Single(path => Path.GetFileName(path) == "first.txt");
        var timestamp = File.GetLastWriteTimeUtc(changed);
        await File.WriteAllTextAsync(changed, "other"); File.SetLastWriteTimeUtc(changed, timestamp);
        var outcome = (await _maintenance.ReclaimAsync([artifact.Id], default,
            new Dictionary<string, string> {{artifact.Id, preview.SelectionFingerprint!}})).Single();
        Assert.IsFalse(outcome.Completed);
        Assert.IsTrue(privatePaths.All(File.Exists));
        Assert.IsTrue(result.Files.All(File.Exists));
    }
}
