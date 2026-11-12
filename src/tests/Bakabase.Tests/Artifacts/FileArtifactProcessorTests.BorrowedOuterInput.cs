using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactProcessorTests
{
    private readonly OuterInputArchives _outerArchives = new();
    private const string OuterOnlyPlan = "{\"requirement\":\"notRequired\",\"steps\":[]}";
    private static FileArtifactProcessingConfig OuterOnlyConfig => new()
    {
        ArchivePolicy = "autoDetect", Recursive = new() {Enabled = true, MinimumFreeBytes = 0},
        StoragePolicy = new() {CleanupIntermediates = false, Materialization = "auto"}
    };

    private async Task<int> BorrowingOwner()
    {
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Waiting};
        _db.WorkflowRuns.Add(run); await _db.SaveChangesAsync(); _db.Entry(run).State = EntityState.Detached;
        return run.Id;
    }

    private Task<FileArtifactProcessingResult> ExecuteOuter(ArtifactRecord input,
        RecursiveArchivePasswordAnswer? answer = null, FileArtifactProcessor? processor = null,
        FileArtifactProcessingConfig? config = null) =>
        (processor ?? new FileArtifactProcessor(_store, new(_outerArchives), new(_outerArchives),
            () => _appData, _services.GetRequiredService<IServiceScopeFactory>())).ExecuteAsync(FileArtifactProcessor.Intent("outer-only", OuterOnlyPlan, null, config ?? OuterOnlyConfig),
            input, OuterOnlyPlan, null, config ?? OuterOnlyConfig, "outer-only-consumer", answer, null, null, default,
            replan: false, stopAfterStepId: null, recovery: null, onlyOuterPackaging: true);

    private async Task<string> OuterWorkspace()
    {
        var path = Directory.GetFiles(Path.Combine(_appData, "artifact-workspaces", "locations"), "operations-*.json").Single();
        return JsonSerializer.Deserialize<FileProcessingWorkspaceResolver.Location>(await File.ReadAllTextAsync(path), Json)!.Directory;
    }

    [TestMethod]
    public async Task Pure_outer_processing_borrows_pinned_input_without_empty_plan_snapshots_and_preserves_lineage()
    {
        var input = await SignedOuterInput(); var owner = await BorrowingOwner();
        using var scope = StorageWriteScope.Enter(new("workflow", owner.ToString()));
        var copies = new OuterInputMaterializer();
        var processor = new FileArtifactProcessor(_store, new(_outerArchives, copies), new(_outerArchives, copies),
            () => _appData, _services.GetRequiredService<IServiceScopeFactory>());
        var result = await ExecuteOuter(input, processor: processor);
        Assert.IsNotNull(result.Artifact);
        var state = await OuterWorkspace();
        Assert.IsTrue(File.Exists(Path.Combine(state, "borrowed-input.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(state, "plan")), "The no-op plan must not materialize input/result copies.");
        Assert.IsFalse(copies.Targets.Any(p => p.StartsWith(Path.Combine(state, "plan") + Path.DirectorySeparatorChar,
            StringComparison.Ordinal)));
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Artifact.Files.Single()));
        Assert.AreEqual("PK\u0003\u0004single", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.AreEqual(1, _outerArchives.Extractions);
        var metadata = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(result.Artifact.MetadataJson!, Json)!;
        Assert.IsTrue(FileDispositionProofs.ValidFor(metadata.FileDisposition, input, result.Artifact));
        Assert.AreEqual("replaced", metadata.FileDisposition!.Entries.Single().Disposition);
        CollectionAssert.AreEqual(new[] {input.Id}, result.Artifact.ParentArtifactIds.ToArray());
        var repeated = await ExecuteOuter(input, processor: processor);
        Assert.AreEqual(result.Artifact.Id, repeated.Artifact?.Id);
        Assert.AreEqual(1, _outerArchives.Extractions);
    }

    [TestMethod]
    public async Task Borrowed_input_stays_durably_pinned_through_password_wait_and_reuses_same_attempt()
    {
        var input = await SignedOuterInput(nested: true); var owner = await BorrowingOwner();
        using var scope = StorageWriteScope.Enter(new("workflow", owner.ToString()));
        var waiting = await ExecuteOuter(input);
        Assert.AreEqual("PasswordUnknown", waiting.WaitReason);
        Assert.IsNotNull(waiting.ArchiveId);
        var operation = await _db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleAsync(o => o.Kind == "files.process");
        var state = await OuterWorkspace();
        var marker = await File.ReadAllTextAsync(Path.Combine(state, "borrowed-input.json"));
        await _db.Set<ArtifactConsumerDbModel>().Where(c => c.OperationId == input.OperationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LeaseUntil, DateTime.UtcNow.AddDays(-1)));
        Assert.IsTrue(await _store.HasActiveConsumersAsync(input.OperationId), "A waiting durable owner must outlive the transient lease.");
        Assert.IsFalse(await _store.TryQuarantineAsync(input.Id));
        var resumed = await ExecuteOuter(input, new(waiting.ArchiveId, "correct"));
        Assert.IsNotNull(resumed.Artifact);
        Assert.AreEqual(operation.Id, resumed.Artifact.OperationId);
        Assert.AreEqual(operation.Attempt, (await _store.GetOperationAsync(operation.Id))!.Attempt);
        Assert.AreEqual(marker, await File.ReadAllTextAsync(Path.Combine(state, "borrowed-input.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(state, "plan")));
        Assert.AreEqual(1, _outerArchives.OuterExtractions);
        Assert.AreEqual(2, _outerArchives.Extractions);
        Assert.AreEqual("PK\u0003\u0004outer", await File.ReadAllTextAsync(input.Files.Single()));
        await _db.WorkflowRuns.Where(r => r.Id == owner).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success));
        Assert.IsFalse(await _store.HasActiveConsumersAsync(input.OperationId));
    }

    [TestMethod]
    public async Task Borrowed_input_changed_with_same_length_and_timestamp_is_not_adopted_or_copied()
    {
        var input = await SignedOuterInput(nested: true); var owner = await BorrowingOwner();
        using var scope = StorageWriteScope.Enter(new("workflow", owner.ToString()));
        var waiting = await ExecuteOuter(input);
        Assert.AreEqual("PasswordUnknown", waiting.WaitReason);
        var state = await OuterWorkspace();
        var marker = await File.ReadAllTextAsync(Path.Combine(state, "borrowed-input.json"));
        var timestamp = File.GetLastWriteTimeUtc(input.Files.Single());
        await File.WriteAllTextAsync(input.Files.Single(), "PK\u0003\u0004other");
        File.SetLastWriteTimeUtc(input.Files.Single(), timestamp);
        var resumed = await ExecuteOuter(input, new(waiting.ArchiveId!, "correct"));
        Assert.IsNull(resumed.Artifact);
        Assert.AreEqual("NeedsReconciliation", resumed.WaitReason);
        Assert.AreEqual("PK\u0003\u0004other", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.AreEqual(1, _outerArchives.Extractions);
        Assert.AreEqual(marker, await File.ReadAllTextAsync(Path.Combine(state, "borrowed-input.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(state, "plan")));
    }

    [TestMethod]
    public async Task Legacy_outer_checkpoint_and_unscoped_callers_keep_their_original_input_layout()
    {
        var input = await SignedOuterInput(nested: true);
        // Without a durable caller the original snapshot execution remains the default.
        var waiting = await ExecuteOuter(input);
        Assert.AreEqual("PasswordUnknown", waiting.WaitReason);
        var state = await OuterWorkspace();
        Assert.IsTrue(Directory.Exists(Path.Combine(state, "plan")));
        Assert.IsFalse(File.Exists(Path.Combine(state, "borrowed-input.json")));
        var planState = Directory.GetFiles(Path.Combine(state, "plan"), "state.json", SearchOption.AllDirectories).Single();
        var prior = await File.ReadAllTextAsync(planState);
        var operation = await _db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleAsync(o => o.Kind == "files.process");
        var owner = await BorrowingOwner();
        using var scope = StorageWriteScope.Enter(new("workflow", owner.ToString()));
        var resumed = await ExecuteOuter(input, new(waiting.ArchiveId!, "correct"));
        Assert.IsNotNull(resumed.Artifact);
        Assert.AreEqual(operation.Id, resumed.Artifact.OperationId);
        Assert.AreEqual(operation.Attempt, (await _store.GetOperationAsync(operation.Id))!.Attempt);
        Assert.IsFalse(File.Exists(Path.Combine(state, "borrowed-input.json")));
        Assert.AreEqual(prior, await File.ReadAllTextAsync(planState));
        Assert.AreEqual(1, _outerArchives.OuterExtractions);
        Assert.AreEqual(2, _outerArchives.Extractions);
    }

    [TestMethod]
    public async Task Missing_borrowed_layout_receipt_does_not_refill_snapshots_or_change_recursive_inputs()
    {
        var input = await SignedOuterInput(nested: true); var owner = await BorrowingOwner();
        using var scope = StorageWriteScope.Enter(new("workflow", owner.ToString()));
        var waiting = await ExecuteOuter(input);
        Assert.AreEqual("PasswordUnknown", waiting.WaitReason);
        var state = await OuterWorkspace();
        var recursiveState = Path.Combine(state, "recursive", "recursive-state.json");
        var before = await File.ReadAllTextAsync(recursiveState);
        File.Delete(Path.Combine(state, "borrowed-input.json"));
        var resumed = await ExecuteOuter(input, new(waiting.ArchiveId!, "correct"));
        Assert.IsNull(resumed.Artifact);
        Assert.AreEqual("NeedsReconciliation", resumed.WaitReason);
        Assert.IsFalse(Directory.Exists(Path.Combine(state, "plan")));
        Assert.AreEqual(before, await File.ReadAllTextAsync(recursiveState));
        Assert.AreEqual(1, _outerArchives.Extractions);
    }

    [TestMethod]
    public async Task Explicit_copy_policy_keeps_snapshot_isolation_even_with_a_durable_caller()
    {
        var input = await SignedOuterInput(nested: true); var owner = await BorrowingOwner();
        using var scope = StorageWriteScope.Enter(new("workflow", owner.ToString()));
        var config = OuterOnlyConfig with {StoragePolicy = OuterOnlyConfig.StoragePolicy! with {Materialization = "copy"}};
        var waiting = await ExecuteOuter(input, config: config);
        Assert.AreEqual("PasswordUnknown", waiting.WaitReason);
        var state = await OuterWorkspace();
        Assert.IsFalse(File.Exists(Path.Combine(state, "borrowed-input.json")));
        var checkpoint = Directory.GetDirectories(Path.Combine(state, "plan")).Single();
        Assert.IsTrue(Directory.Exists(Path.Combine(checkpoint, "inputs")));
        Assert.IsTrue(Directory.Exists(Path.Combine(checkpoint, "result")));
        Assert.AreEqual("PK\u0003\u0004outer", await File.ReadAllTextAsync(input.Files.Single()));
    }

    private async Task<ArtifactRecord> SignedOuterInput(bool nested = false)
    {
        var directory = Path.Combine(_root, "signed-download"); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "input.zip");
        await File.WriteAllTextAsync(file, "PK\u0003\u0004" + (nested ? "outer" : "single"));
        return await _store.AdoptAsync(new("signed-input", "signed-download", "download", "signed-v1"),
            new(directory, [file], Managed: true), "signed-download-owner");
    }

    private sealed class OuterInputArchives : IArchiveExtractionService
    {
        internal int Extractions { get; private set; }
        internal int OuterExtractions { get; private set; }
        public Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string file,
            IReadOnlyList<string?> passwords, CancellationToken ct) => Task.FromResult<ArchiveFormatProbeResult?>(new(".zip"));
        public async Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct)
        {
            var locked = await File.ReadAllTextAsync(file, ct) == "PK\u0003\u0004inner";
            return new(!locked || candidates.Contains("correct"), locked ? "correct" : null, [], "", "Password required.");
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? onProgress, CancellationToken ct)
        {
            Extractions++;
            if (await File.ReadAllTextAsync(request.Files.Single(), ct) == "PK\u0003\u0004outer")
            {
                OuterExtractions++;
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "inner.zip"), "PK\u0003\u0004inner", ct);
            }
            else await File.WriteAllTextAsync(Path.Combine(request.Directory, "payload.txt"), "payload", ct);
            return new(true, request.Directory, "");
        }
    }

    private sealed class OuterInputMaterializer : IFileArtifactMaterializer
    {
        internal readonly List<string> Targets = [];
        public Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Targets.Add(target); File.Copy(source, target, overwrite: false);
            return Task.FromResult(new FileMaterializationResult("copy", new FileInfo(target).Length));
        }
    }
}
