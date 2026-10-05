using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

[TestClass]
public class FileProcessingPlanTests
{
    private string _root = null!;
    private string _input = null!;
    private string _state = null!;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "BakabasePlan_" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_root, "downloads");
        _state = Path.Combine(_root, "state");
        Directory.CreateDirectory(_input);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private async Task<string> Input(string name, string content = "archive")
    {
        var path = Path.Combine(_input, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static PostExtractionPlan NestedPlan() => new()
    {
        Requirement = "required",
        Steps = [
            new() {Id = "rename1", Op = "renameExtension", Extension = ".7z", Selector = "outer.png"},
            new() {Id = "extract1", Op = "extractArchive", Input = "rename1", Password = "first"},
            new() {Id = "rename2", Op = "renameExtension", Input = "extract1", Selector = "inner.png", Extension = ".zip"},
            new() {Id = "extract2", Op = "extractArchive", Input = "rename2", Password = "second"}
        ]
    };

    [TestMethod]
    public async Task OrderedNestedPlanUsesEachPasswordAndPreservesOriginalsAndUnselectedFiles()
    {
        var original = await Input("outer.png");
        var note = await Input("download-note.txt", "keep");
        var archives = new FakeArchives();
        var executor = new FileProcessingPlanExecutor(archives);
        var result = await executor.ExecuteAsync(NestedPlan(), _input, [original, note], _state);
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(2, archives.Extractions);
        CollectionAssert.AreEqual(new[] {"first", "second"}, archives.Passwords.ToArray());
        CollectionAssert.AreEquivalent(new[] {"final.txt", "readme.txt", "download-note.txt"},
            result.Files.Select(Path.GetFileName).ToArray());
        Assert.IsTrue(File.Exists(original));
        Assert.IsFalse(File.Exists(Path.Combine(_input, "outer.7z")), "renames apply to the staged inputs");
        var repeated = await executor.ExecuteAsync(NestedPlan(), _input, [original, note], _state);
        Assert.IsTrue(repeated.Completed);
        Assert.AreEqual(2, archives.Extractions, "a finished plan is not executed again");
    }

    [TestMethod]
    public async Task RenameMoveAndExtractionCanBeFreelyCombinedWithPerStepOutputs()
    {
        var original = await Input("outer.png");
        var plan = new PostExtractionPlan {Requirement = "required", Steps = [
            new() {Id = "name1", Op = "renameFile", TargetName = "outer.7z"},
            new() {Id = "move1", Op = "moveFile", Input = "name1", TargetDirectory = "packages"},
            new() {Id = "open1", Op = "extractArchive", Input = "move1", Password = "first"},
            new() {Id = "name2", Op = "renameFile", Input = "open1", Selector = "inner.png", TargetName = "inner.zip"},
            new() {Id = "move2", Op = "moveFile", Input = "name2", TargetDirectory = "nested"},
            new() {Id = "open2", Op = "extractArchive", Input = "move2", Password = "second"},
            new() {Id = "move3", Op = "moveFile", Input = "open2", TargetDirectory = "finished"},
            new() {Id = "name3", Op = "renameFile", Input = "move3", TargetName = "ready.txt"}]};
        var archives = new FakeArchives();
        var executor = new FileProcessingPlanExecutor(archives);
        var result = await executor.ExecuteAsync(plan, _input, [original], _state);
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEquivalent(new[] {"finished/ready.txt", "packages/outer/readme.txt"},
            result.Files.Select(f => Path.GetRelativePath(result.OutputDirectory!, f).Replace('\\', '/')).ToArray());
        Assert.AreEqual("payload", await File.ReadAllTextAsync(result.Files.Single(f => Path.GetFileName(f) == "ready.txt")));
        Assert.AreEqual("archive", await File.ReadAllTextAsync(original));
        var repeated = await executor.ExecuteAsync(plan, _input, [original], _state);
        Assert.IsTrue(repeated.Completed);
        Assert.AreEqual(2, archives.Extractions, "completed move/rename/extract steps are not replayed");
    }

    [TestMethod]
    public async Task AFileProcessingPlanCanRenameAndMoveWithoutAnyExtraction()
    {
        var original = await Input("data.009", "not an archive part");
        var plan = new PostExtractionPlan {Requirement = "required", Steps = [
            new() {Id = "rename", Op = "renameFile", TargetName = "report.txt"},
            new() {Id = "move", Op = "moveFile", Input = "rename", TargetDirectory = "documents/reports"}]};
        var archives = new FakeArchives();
        var result = await new FileProcessingPlanExecutor(archives).ExecuteAsync(plan, _input, [original], _state);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual("documents/reports/report.txt", Path.GetRelativePath(result.OutputDirectory!, result.Files.Single()).Replace('\\', '/'));
        Assert.AreEqual(0, archives.Extractions);
        Assert.IsTrue(File.Exists(original));
    }

    [TestMethod]
    public async Task RenameAndMoveRejectCollisionsWithoutReplacingFiles()
    {
        var source = await Input("source.txt", "source");
        var occupied = await Input("occupied.txt", "keep");
        var rename = new PostExtractionPlan {Requirement = "required", Steps = [
            new() {Id = "rename", Op = "renameFile", Selector = "source.txt", TargetName = "occupied.txt"}]};
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new FileProcessingPlanExecutor(new FakeArchives())
            .ExecuteAsync(rename, _input, [source, occupied], _state));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(occupied));
        var first = await Input("a/same.txt", "first");
        var second = await Input("b/same.txt", "second");
        var move = new PostExtractionPlan {Requirement = "required", Steps = [
            new() {Id = "move", Op = "moveFile", TargetDirectory = "destination"}]};
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new FileProcessingPlanExecutor(new FakeArchives())
            .ExecuteAsync(move, _input, [first, second], _state));
        Assert.AreEqual("first", await File.ReadAllTextAsync(first));
        Assert.AreEqual("second", await File.ReadAllTextAsync(second));
    }

    [TestMethod]
    public async Task RenameOutputsKeepEarlierInputsAvailableForAnotherBranch()
    {
        var original = await Input("archive.png", "same bytes in both branches");
        var plan = new PostExtractionPlan {Requirement = "required", Steps = [
            new() {Id = "sevenZip", Op = "renameExtension", Extension = ".7z"},
            new() {Id = "zip", Op = "renameExtension", Input = "download", Extension = ".zip"}]};
        var result = await new FileProcessingPlanExecutor(new FakeArchives())
            .ExecuteAsync(plan, _input, [original], _state);
        Assert.IsTrue(result.Completed, result.Message);
        CollectionAssert.AreEquivalent(new[] {"archive.7z", "archive.zip"}, result.Files.Select(Path.GetFileName).ToArray());
        foreach (var output in result.Files) Assert.AreEqual("same bytes in both branches", await File.ReadAllTextAsync(output));
        Assert.IsTrue(File.Exists(original));
    }

    [TestMethod]
    public async Task RenamingAWholeVolumeSetKeepsThePartsTogetherForExtraction()
    {
        var first = await Input("work.png.001");
        var second = await Input("work.png.002");
        var plan = new PostExtractionPlan {Requirement = "required", Steps = [
            new() {Id = "rename", Op = "renameExtension", Extension = ".7z"},
            new() {Id = "extract", Op = "extractArchive", Input = "rename", Password = "second"}]};
        var archives = new FakeArchives();
        var result = await new FileProcessingPlanExecutor(archives).ExecuteAsync(plan, _input, [first, second], _state);
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(1, archives.Extractions);
        Assert.AreEqual(2, archives.GroupSizes.Single());
    }

    [TestMethod]
    public async Task AlreadyProcessedManualInputDoesNotRequireAnExtractionPlan()
    {
        await Input("finished.mp4");
        var payload = (FileProcessingPlanPayload)new FileProcessingPlanTrigger().BuildManualPayload(null,
            JsonSerializer.Serialize(new FileProcessingPlanPayload {Directory = _input, AlreadyProcessed = true}, Json));
        Assert.AreEqual("notRequired", JsonSerializer.Deserialize<PostExtractionPlan>(payload.ExtractionPlanJson, Json)!.Requirement);
    }

    [TestMethod]
    public async Task PasswordWaitRestartsFromTheIncompleteLayerUsingPersistedOperations()
    {
        var original = await Input("outer.png");
        var archives = new FakeArchives {SecondPassword = "corrected"};
        var result = await new FileProcessingPlanExecutor(archives)
            .ExecuteAsync(NestedPlan(), _input, [original], _state);
        Assert.IsFalse(result.Completed);
        Assert.IsTrue(result.NeedsPassword);
        Assert.AreEqual("extract2", result.StepId);
        Assert.AreEqual(1, archives.Extractions);
        var resumed = await new FileProcessingPlanExecutor(archives)
            .ExecuteAsync(NestedPlan(), _input, [original], _state, "corrected");
        Assert.IsTrue(resumed.Completed, resumed.Message);
        Assert.AreEqual(2, archives.Extractions, "the outer archive must not be extracted again");
    }

    [TestMethod]
    public async Task UnknownPlanWaitsAndExplicitlyUnneededPlanPassesThrough()
    {
        var file = await Input("video.mp4");
        var archives = new FakeArchives();
        var executor = new FileProcessingPlanExecutor(archives);
        var unknown = await executor.ExecuteAsync(new PostExtractionPlan(), _input, [file], _state);
        Assert.IsFalse(unknown.Completed);
        Assert.IsFalse(Directory.Exists(_state));
        var complete = await executor.ExecuteAsync(new PostExtractionPlan {Requirement = "notRequired"}, _input, [file], _state);
        Assert.IsTrue(complete.Completed);
        Assert.AreEqual("video.mp4", Path.GetFileName(complete.Files.Single()));
        Assert.AreEqual("archive", await File.ReadAllTextAsync(complete.Files.Single()));
        Assert.AreEqual(0, archives.Extractions);
    }

    [TestMethod]
    public async Task NotRequiredPlanStagesOnlyTheSelectedFilesForLaterPlacement()
    {
        var selected = await Input("selected.mp4");
        var unrelated = await Input("another-resource.mp4", "untouched");
        var result = await new FileProcessingPlanExecutor(new FakeArchives()).ExecuteAsync(
            new PostExtractionPlan {Requirement = "notRequired"}, _input, [selected], _state);
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(1, Directory.GetFiles(result.OutputDirectory!, "*", SearchOption.AllDirectories).Length);
        Assert.AreEqual("selected.mp4", Path.GetFileName(result.Files.Single()));
        Assert.AreEqual("untouched", await File.ReadAllTextAsync(unrelated));
    }

    [TestMethod]
    public async Task UnrelatedDirectoryAndTraversalSelectorsCannotBeProcessed()
    {
        var outside = Path.Combine(_root, "outside.png");
        await File.WriteAllTextAsync(outside, "private");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new FileProcessingPlanExecutor(new FakeArchives())
            .ExecuteAsync(NestedPlan(), _input, [outside], _state));
        var unsafePlan = new PostExtractionPlan {Requirement = "required", Steps = [
            new() {Id = "x", Op = "renameExtension", Selector = "../outside.png", Extension = ".zip"}]};
        Assert.ThrowsException<InvalidOperationException>(() => FileProcessingPlanExecutor.Validate(unsafePlan));
        Assert.AreEqual("private", await File.ReadAllTextAsync(outside));
    }

    [TestMethod]
    public async Task ManualTriggerProducesOneResourceBindingAndIgnoresEvents()
    {
        var first = await Input("one.txt");
        var second = await Input("two.txt");
        var trigger = new FileProcessingPlanTrigger();
        var payload = trigger.BuildManualPayload(null, JsonSerializer.Serialize(new FileProcessingPlanPayload
        {
            Directory = _input, BindingId = "post-resource-1", ResourceId = 123,
            ExtractionPlanJson = JsonSerializer.Serialize(new PostExtractionPlan {Requirement = "notRequired"}, Json)
        }, Json));
        var item = (AcquisitionWorkItem)trigger.ExtractItems(payload).Single();
        Assert.AreEqual(123, item.ResourceId);
        Assert.AreEqual("post-resource-1", item.LeadValue);
        CollectionAssert.AreEquivalent(new[] {first, second}, item.Files.ToArray());
        Assert.AreEqual(WorkflowActivationMode.Manual, trigger.ActivationMode);
        Assert.IsFalse(trigger.Matches(payload, null));
    }

    [TestMethod]
    public async Task SeededManualWorkflowRunsWithoutAResourceOrAcquisitionTask()
    {
        var selected = await Input("selected.mp4");
        await Input("unrelated.mp4");
        var services = await TestServiceBuilder.BuildServiceProvider();
        await services.GetRequiredService<FileProcessingWorkflowSeeder>().SeedAsync();
        var db = services.GetRequiredService<BakabaseDbContext>();
        var definition = await db.Set<WorkflowDefinitionDbModel>().AsNoTracking().SingleAsync(d =>
            d.IsBuiltin && d.TriggerKind == FileProcessingPlanTrigger.TriggerKind);
        Assert.IsTrue(definition.Enabled);
        var run = await services.GetRequiredService<IWorkflowDefinitionService>().RunManuallyAsync(definition.Id,
            JsonSerializer.Serialize(new FileProcessingPlanPayload
            {
                Directory = _input, Files = [selected], ResourceId = 0,
                ExtractionPlanJson = JsonSerializer.Serialize(new PostExtractionPlan {Requirement = "notRequired"}, Json)
            }, Json));
        var arguments = new BTaskArgs(new PauseToken(), CancellationToken.None,
            new BTask("processing-plan-test", () => "processing-plan-test"), _ => Task.CompletedTask, services);
        await services.GetRequiredService<WorkflowRunner<BakabaseDbContext>>().ExecuteAsync(run.Id, arguments);
        var saved = await db.Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.AreEqual(WorkflowRunStatus.Success, saved.Status, saved.ErrorMessage);
        Assert.AreEqual(1, saved.InputCount);
        var output = JsonSerializer.Deserialize<List<AcquisitionWorkItem>>(saved.OutputItemsJson!, Json)!.Single();
        Assert.AreEqual(0, output.ResourceId);
        Assert.AreEqual("selected.mp4", Path.GetFileName(output.Files.Single()));
        Assert.AreNotEqual(_input, output.ExtractedDirectory, "the real unpack node must stage only the bound files");
        Assert.IsTrue(File.Exists(output.Files.Single()));
        Assert.AreEqual(0, await db.Set<AcquisitionTaskDbModel>().CountAsync());
    }

    [TestMethod]
    public async Task VolumeGapsAreRejectedAndASelectedVolumeExpandsToItsWholeGroup()
    {
        var first = await Input("work.part1.rar");
        var third = await Input("work.part3.rar");
        Assert.ThrowsException<InvalidOperationException>(() => FileProcessingFiles.ValidateVolumes([first, third]));
        var second = await Input("work.part2.rar");
        CollectionAssert.AreEquivalent(new[] {first, second, third}, FileProcessingFiles.ExpandVolumes([first]).ToArray());
        FileProcessingFiles.ValidateVolumes([first, second, third]);
    }

    private sealed class FakeArchives : IArchiveExtractionService
    {
        public int Extractions { get; private set; }
        public string SecondPassword { get; init; } = "second";
        public List<string> Passwords { get; } = [];
        public List<int> GroupSizes { get; } = [];
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? onOutput, CancellationToken ct)
        {
            var password = Path.GetFileName(entryFile).StartsWith("outer") ? "first" : SecondPassword;
            return Task.FromResult(new ArchivePasswordProbe(candidates.Contains(password), password,
                candidates.Where(c => c != null && c != password).Cast<string>().ToList(), "", "password needed"));
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? onProgress, CancellationToken ct)
        {
            Extractions++;
            GroupSizes.Add(request.Files.Count);
            Passwords.Add(request.Password!);
            if (Path.GetFileName(request.Files[0]).StartsWith("outer"))
            {
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "inner.png"), "inner archive", ct);
                await File.WriteAllTextAsync(Path.Combine(request.Directory, "readme.txt"), "keep this sibling", ct);
            }
            else await File.WriteAllTextAsync(Path.Combine(request.Directory, "final.txt"), "payload", ct);
            return new(true, request.Directory, "");
        }
    }
}
