using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Tests.Artifacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private sealed class VolumeProcessingServices(IServiceProvider services, IFileArtifactProcessor processor) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(IFileArtifactProcessor) ? processor : services.GetService(type);
    }

    private sealed class VolumeArchiveTool : IArchiveExtractionService
    {
        internal List<string[]> Inputs { get; } = [];
        internal int Extractions { get; private set; }

        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct)
        {
            var password = candidates.FirstOrDefault(p => p == "fixture-password");
            return Task.FromResult(new ArchivePasswordProbe(password != null, password, [], "", "Password required."));
        }

        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct, ArchiveOpenOptions? options)
        {
            Assert.AreEqual("-t7z.split", options?.TypeArgument);
            return ProbePasswordAsync(file, candidates, output, ct);
        }

        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Assert.AreEqual("fixture-password", request.Password);
            Assert.AreEqual("-t7z.split", request.OpenOptions?.TypeArgument);
            Assert.IsFalse(request.DeleteAfterDecompression);
            Inputs.Add(request.Files.Select(file => new FileInfo(file).ResolveLinkTarget(true)?.FullName ?? file).ToArray());
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "content.txt"), "payload verification", ct);
            Extractions++;
            return new(true, request.Directory, "");
        }
    }

    private sealed class VolumeReservePolicy : IStorageWritePolicyProvider
    {
        internal const long Reserve = 50L * 1024 * 1024 * 1024;
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) => Task.FromResult(Reserve);
    }

    [DataTestMethod]
    [DataRow("normal")]
    [DataRow("resumePassword")]
    [DataRow("explicitCopy")]
    [DataRow("namedWithoutAttachment")]
    [DataRow("incompleteReceipt")]
    [DataRow("changedVolume")]
    [DataRow("missingVolume")]
    public async Task Acquired_split_7z_uses_original_volumes_or_explicit_private_copies(string scenario)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Split archive volume views require permission to create symbolic links.");
        var forceCopy = scenario == "explicitCopy";
        var hasAttachment = scenario != "namedWithoutAttachment";
        var resumePassword = scenario is "resumePassword" or "incompleteReceipt" or "changedVolume" or "missingVolume" or "namedWithoutAttachment";
        var (task, old, _) = await Failed();
        var download = Directory.CreateDirectory(Path.Combine(_root, "download-volume")).FullName;
        var workspace = Directory.CreateDirectory(Path.Combine(_root, "processing-volume")).FullName;
        var volumes = FileProcessingMaskedSevenZipTests.MakeVolumes(download);
        var note = Path.Combine(download, "readme.txt");
        if (hasAttachment)
            await File.WriteAllTextAsync(note, "retained attachment");
        else
        {
            var namedHead = Path.ChangeExtension(volumes[1], "001");
            File.Move(volumes[0], namedHead);
            volumes[0] = namedHead;
        }
        var sourceFiles = hasAttachment ? volumes.Append(note) : volumes;
        var originals = sourceFiles.ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        task = await BindCurrentStage(task, "download", download);
        task = await BindCurrentStage(task, "processing", workspace, taskAllocation: true);
        var revision = await Plans.ReplanAsync(task.Id, new(task.Task.PlanRevision, old.Id, "Extract these registered split volumes.",
            [new("main", new() {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive", Input = "download",
                Password = resumePassword ? null : "fixture-password"}]}, [], InputMode: "stageDirectory", InputStage: "download")],
            EnableAdaptiveAssessment: false), default);
        // Compile the frozen user-selected placement and copy policy as the workflow host does.
        var frozen = revision.Plan! with {StoragePolicy = new() {ProcessingWorkspaceDirectory = workspace,
            Materialization = forceCopy ? "copy" : "auto", CleanupIntermediates = false}};
        var compiled = AcquisitionPlanCompiler.Compile(frozen);
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revision.Task.PlanRevision, frozen, revision.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(),
                Revision = revision.Task.PlanRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)}]};
        Db.Add(run); await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, run.Id));

        var tool = new VolumeArchiveTool();
        var inputBudget = forceCopy ? originals.Keys.Sum(file => new FileInfo(file).Length) : hasAttachment ? new FileInfo(note).Length : 0;
        var inputAdmissionChecks = 0;
        var guard = new StorageWriteGuard(new VolumeReservePolicy(), destination =>
        {
            if (Path.GetFileName(destination) != "inputs") return ("workspace", VolumeReservePolicy.Reserve + 4096);
            inputAdmissionChecks++;
            return ("workspace", VolumeReservePolicy.Reserve + inputBudget);
        }, TimeSpan.FromSeconds(1));
        var processor = new FileArtifactProcessor(Artifacts, new(tool, writeGuard: guard), new(tool), () => Path.Combine(_root, "appdata"),
            _services.GetRequiredService<IServiceScopeFactory>());
        var services = new VolumeProcessingServices(_services, processor);
        WorkflowExecutionContext Context(int node, string? prompt = null) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[node].ConfigJson,
            Services = services, ResumePromptJson = prompt, Logger = NullLogger.Instance};
        var continued = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        Assert.IsNull(continued.Suspension, continued.Suspension?.PromptJson);
        var activity = new AcquisitionPlanProcessingActivity();
        var result = await activity.ProcessItemAsync(Context(1), continued.Replacement!, default);
        if (resumePassword)
        {
            Assert.IsNotNull(result.Suspension);
            var prompt = JsonDocument.Parse(result.Suspension.PromptJson!).RootElement;
            Assert.AreEqual("PasswordUnknown", prompt.GetProperty("reason").GetString(), result.Suspension.PromptJson);
            var input = (await Artifacts.GetArtifactAsync(revision.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
            Assert.IsTrue(await Artifacts.HasActiveConsumersAsync(input.OperationId));
            Assert.AreEqual(0, tool.Extractions);
            var journalFile = Directory.EnumerateFiles(workspace, "state.json", SearchOption.AllDirectories).Single();
            var journal = JsonNode.Parse(await File.ReadAllTextAsync(journalFile))!.AsObject();
            Assert.AreEqual(input.Id, (string?)journal["readOnlyVolumeArtifactId"]);
            CollectionAssert.AreEquivalent(volumes.Select(path => Path.GetRelativePath(download, path)).ToArray(),
                journal["readOnlyVolumeMembers"]!.AsArray().Select(value => (string?)value).ToArray());
            var inputs = Path.Combine(Path.GetDirectoryName(journalFile)!, "inputs");
            CollectionAssert.AreEquivalent(hasAttachment ? new[] {"readme.txt"} : Array.Empty<string>(),
                Directory.EnumerateFiles(inputs, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray());
            if (scenario == "incompleteReceipt")
            {
                journal.Remove("readOnlyVolumeArtifactId");
                await File.WriteAllTextAsync(journalFile, journal.ToJsonString());
            }
            if (scenario == "missingVolume") File.Delete(volumes[^1]);
            if (scenario == "changedVolume")
            {
                // Offset 32 changes payload bytes while leaving the 7z start and next-header CRCs intact.
                var bytes = await File.ReadAllBytesAsync(volumes[0]);
                bytes[32] ^= 1;
                await File.WriteAllBytesAsync(volumes[0], bytes);
            }
            var signal = JsonSerializer.Serialize(new CloudFileProcessingSignal {TargetId = "main", Password = "fixture-password"}, WorkflowJson.Options);
            result = await activity.ResumeAsync(Context(1, result.Suspension.PromptJson), result.Replacement ?? continued.Replacement!, signal, default);
        }
        Assert.IsTrue(inputAdmissionChecks > 0, "The snapshot must pass the live reserve check.");
        if (scenario is "incompleteReceipt" or "changedVolume" or "missingVolume")
        {
            Assert.IsNotNull(result.Suspension);
            var failure = JsonDocument.Parse(result.Suspension.PromptJson!).RootElement;
            Assert.AreEqual("NeedsReconciliation", failure.GetProperty("reason").GetString(), result.Suspension.PromptJson);
            Assert.AreEqual(0, tool.Extractions);
            var waiting = ((DownloadWorkset)(result.Replacement ?? continued.Replacement!)).Targets.Single();
            Assert.IsNull(waiting.ProcessedArtifactId);
            Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
            return;
        }
        Assert.IsNull(result.Suspension, result.Suspension?.PromptJson);
        var output = ((DownloadWorkset)result.Replacement!).Targets.Single();
        Assert.AreEqual(1, tool.Extractions);
        if (forceCopy)
            Assert.IsFalse(volumes.Intersect(tool.Inputs.Single()).Any(), "Explicit copy must isolate every volume: " + string.Join(", ", tool.Inputs.Single()));
        else
            CollectionAssert.AreEqual(volumes, tool.Inputs.Single());
        var processed = (await Artifacts.GetArtifactAsync(output.ProcessedArtifactId!))!;
        StringAssert.StartsWith(processed.RootDirectory, workspace);
        CollectionAssert.AreEquivalent(hasAttachment ? new[] {"readme.txt", "content.txt"} : ["content.txt"],
            processed.Files.Select(Path.GetFileName).ToArray());
        if (hasAttachment)
            Assert.AreEqual("retained attachment", await File.ReadAllTextAsync(processed.Files.Single(f => Path.GetFileName(f) == "readme.txt")));
        CollectionAssert.AreEquivalent(originals.Keys.ToArray(), Directory.GetFiles(download, "*", SearchOption.AllDirectories));
        foreach (var (file, expected) in originals)
            Assert.AreEqual(expected, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file))));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }


    [TestMethod]
    public async Task Wrapped_registered_volumes_continue_across_two_extraction_boundaries_without_copying_the_family()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (OperatingSystem.IsWindows() || !File.Exists(executable)) Assert.Inconclusive("Requires a local 7-Zip executable and private symbolic links.");
        var (task, old, _) = await Failed();
        var download = Directory.CreateDirectory(Path.Combine(_root, "registered")).FullName;
        var workspace = Directory.CreateDirectory(Path.Combine(_root, "processing")).FullName;
        // Two independently hashed local names from one split 7-Zip containing a real ZIP.
        var archive = Convert.FromBase64String("N3q8ryccAARs92uHkgAAAAAAAABeAAAAAAAAAMHgqYZQSwMEFAAAAAAAAAAhKPoFW9MaAAAAGgAAAAsAAABjb250ZW50LnR4dG5lc3RlZCB2b2x1bWUgdmVyaWZpY2F0aW9uUEsBAhQDFAAAAAAAAAAhKPoFW9MaAAAAGgAAAAsAAAAAAAAAAAAAAIABAAAAAGNvbnRlbnQudHh0UEsFBgAAAAABAAEAOQAAAEMAAAAAAAEEBgABCYCSAAcLAQABAQAMgJIACAoByhaMxQAABQEZDAAAAAAAAAAAAAAAABElAGkAbgBuAGUAcgAtAGYAaQB4AHQAdQByAGUALgB6AGkAcAAAABUGAQAggKSBAAA=");
        var cut = (archive.Length + 1) / 2;
        var first = Path.Combine(download, "bundle.001~85109d927455cbcf.PDF");
        var last = Path.Combine(download, "bundle.002~ccedbcfdc0846868.PDF");
        await File.WriteAllBytesAsync(first, archive[..cut]);
        await File.WriteAllBytesAsync(last, archive[cut..]);
        var notes = Enumerable.Range(1, 17).Select(n => Path.Combine(download, $"guide-{n}.pdf")).ToArray();
        foreach (var note in notes) await File.WriteAllTextAsync(note, "%PDF " + Path.GetFileName(note));
        var before = Directory.GetFiles(download).ToDictionary(f => Path.GetFileName(f)!, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
        task = await BindCurrentStage(task, "download", download);
        task = await BindCurrentStage(task, "processing", workspace, taskAllocation: true);
        var revision = await Plans.ReplanAsync(task.Id, new(task.Task.PlanRevision, old.Id, "Extract the two nested archives.",
            [new("main", new() {Requirement = "required", Steps = [new() {Id = "outer", Op = "extractArchive", Input = "download"},
                new() {Id = "inner", Op = "extractArchive", Input = "outer"}]}, [], InputMode: "stageDirectory", InputStage: "download")],
            EnableAdaptiveAssessment: false), default);
        var frozen = revision.Plan! with {StoragePolicy = new() {ProcessingWorkspaceDirectory = workspace, Materialization = "auto"}};
        var compiled = AcquisitionPlanCompiler.Compile(frozen);
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revision.Task.PlanRevision, frozen, revision.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(),
                Revision = revision.Task.PlanRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)}]};
        Db.Add(run); await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, run.Id));
        var service = new ArchiveExtractionService(new TestableCompressedFileService(executable!));
        var processor = new FileArtifactProcessor(Artifacts, new(service), new(service), () => Path.Combine(_root, "appdata"),
            _services.GetRequiredService<IServiceScopeFactory>());
        var services = new VolumeProcessingServices(_services, processor);
        WorkflowExecutionContext Context(int node) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[node].ConfigJson,
            Services = services, Logger = NullLogger.Instance};
        var continued = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        Assert.IsNull(continued.Suspension, continued.Suspension?.PromptJson);
        var activity = new AcquisitionPlanProcessingActivity();
        var outer = await activity.ProcessItemAsync(Context(1), continued.Replacement!, default);
        Assert.IsNull(outer.Suspension, outer.Suspension?.PromptJson);
        var result = await activity.ProcessItemAsync(Context(2), outer.Replacement!, default);
        Assert.IsNull(result.Suspension, result.Suspension?.PromptJson);
        var processed = (await Artifacts.GetArtifactAsync(((DownloadWorkset)result.Replacement!).Targets.Single().ProcessedArtifactId!))!;
        CollectionAssert.AreEquivalent(notes.Select(Path.GetFileName).Append("content.txt").ToArray(), processed.Files.Select(Path.GetFileName).ToArray());
        foreach (var file in processed.Files)
            Assert.AreEqual(Path.GetFileName(file)=="content.txt" ? "nested volume verification" : "%PDF " + Path.GetFileName(file), await File.ReadAllTextAsync(file));
        var journalFile = Directory.EnumerateFiles(workspace, "state.json", SearchOption.AllDirectories).Single();
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(journalFile))!.AsObject();
        CollectionAssert.AreEquivalent(new[]{Path.GetFileName(first),Path.GetFileName(last)},
            journal["readOnlyVolumeMembers"]!.AsArray().Select(n => (string?)n).ToArray());
        CollectionAssert.AreEquivalent(notes.Select(Path.GetFileName).ToArray(),
            Directory.GetFiles(Path.Combine(Path.GetDirectoryName(journalFile)!, "inputs"), "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray());
        Assert.AreEqual(2 + notes.Length, Directory.GetFiles(download).Length);
        foreach (var file in Directory.GetFiles(download))
            Assert.AreEqual(before[Path.GetFileName(file)],Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
    }

    [TestMethod]
    public async Task Existing_private_split_volume_snapshot_remains_authoritative_on_resume()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Split archive volume views require permission to create symbolic links.");
        var source = Directory.CreateDirectory(Path.Combine(_root, "old-download")).FullName;
        var work = Directory.CreateDirectory(Path.Combine(_root, "old-attempt")).FullName;
        var volumes = FileProcessingMaskedSevenZipTests.MakeVolumes(source);
        var attachment = Path.Combine(source, "readme.txt");
        await File.WriteAllTextAsync(attachment, "already snapshotted");
        var tool = new VolumeArchiveTool();
        var executor = new FileProcessingPlanExecutor(tool);
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive", Input = "download"}]};
        var files = volumes.Append(attachment).ToList();
        var paused = await executor.ExecuteAsync(plan, source, files, work);
        Assert.IsTrue(paused.NeedsPassword);
        var journal = Directory.EnumerateFiles(work, "state.json", SearchOption.AllDirectories).Single();
        var inputs = Path.Combine(Path.GetDirectoryName(journal)!, "inputs");
        var copies = volumes.Select(original => Path.Combine(inputs, Path.GetRelativePath(source, original))).ToArray();
        Assert.IsTrue(copies.All(File.Exists));
        var result = await executor.ExecuteAsync(plan, source, files, work, "fixture-password",
            resolveReadOnlyArchiveInput: _ => throw new AssertFailedException("An existing private snapshot must not acquire a new source."));
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEqual(copies, tool.Inputs.Single());
        CollectionAssert.AreEquivalent(new[] {"readme.txt", "content.txt"}, result.Files.Select(Path.GetFileName).ToArray());
    }
}
