using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Workflow;
using Bakabase.Service.Components.Workflow.Fs;
using Bakabase.Service.Components.Workflow.Text;
using Bakabase.Service.Components.Workflow.Triggers;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Batch 4 of the file-cleaning vertical: the variable bag (E4) and chain expansion (E2) —
/// capture into the bag, descend into children who inherit it, rebuild names from templates
/// that combine what different levels knew.
/// </summary>
[TestClass]
public sealed class WorkflowVariablesTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider _sp = null!;
    private string _root = null!;

    /// <summary>Declares OneToOne but returns an expansion — the runner's cardinality guard
    /// exists to catch exactly this declaration bug.</summary>
    private sealed class LyingExpander : IWorkflowActivity
    {
        public const string ActivityKind = "transform.test.lyingExpander";
        public string Kind => ActivityKind;
        public string DisplayName => "Lying expander (test)";
        public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
        public string Group => "test";
        public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
            CancellationToken ct) =>
            Task.FromResult(WorkflowItemOutcome.ExpandTo([item]));
    }

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton<IWorkflowActivity, LyingExpander>());
        _root = Path.Combine(Path.GetTempPath(), $"BakabaseVars_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }

    private static BTaskArgs BuildArgs(IServiceProvider sp) => new(
        new PauseToken(), CancellationToken.None, new BTask("test", () => "test"),
        _ => Task.CompletedTask, sp);

    private string ScanFilterJson(FsScanTarget target, int depth = 1) =>
        JsonSerializer.Serialize(new {roots = new[] {_root}, target, depth}, Json);

    private async Task<int> CreateDefinition(string filterJson, params WorkflowActivityInputModel[] activities) =>
        (await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = $"t-{Guid.NewGuid():N}",
                TriggerKind = FsWorkflowKinds.TriggerManualScan,
                TriggerFilterJson = filterJson,
                Enabled = false,
                Activities = activities.ToList()
            })).Id;

    private async Task<int> RunChain(string filterJson, params WorkflowActivityInputModel[] activities)
    {
        var defId = await CreateDefinition(filterJson, activities);
        var trigger = _sp.GetRequiredService<IWorkflowTriggerRegistry>().Get(FsWorkflowKinds.TriggerManualScan)!;
        var payload = trigger.BuildManualPayload(filterJson, null);

        int runId;
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var run = new WorkflowRunDbModel
            {
                WorkflowDefinitionId = defId,
                Status = WorkflowRunStatus.Pending,
                StartedAt = DateTime.Now,
                PayloadJson = JsonSerializer.Serialize(payload, Json)
            };
            db.Set<WorkflowRunDbModel>().Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        WorkflowRunner<BakabaseDbContext> runner;
        await using (var scope = _sp.CreateAsyncScope())
        {
            runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner<BakabaseDbContext>>();
        }

        await runner.ExecuteAsync(runId, BuildArgs(_sp));
        return runId;
    }

    private async Task<WorkflowRunDbModel> Run(int runId)
    {
        await using var scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
            .Set<WorkflowRunDbModel>().AsNoTracking().FirstAsync(r => r.Id == runId);
    }

    private async Task<List<Bakabase.Abstractions.Models.Db.FileRenameEntry>> Plan(int runId)
    {
        await using var scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFileRenameEntryService>().GetByRunId(runId);
    }

    private static WorkflowActivityInputModel Activity(string kind, object? config = null,
        WorkflowActivityErrorBehavior onError = WorkflowActivityErrorBehavior.Fail) => new()
    {
        Kind = kind,
        ConfigJson = config == null ? "{}" : JsonSerializer.Serialize(config, Json),
        OnItemError = onError,
    };

    /// <summary>
    /// The design's flagship shape (13-node example, compressed): capture at the directory
    /// level, descend, capture at the file level, rebuild the file name from both levels.
    /// Two shows in one scan also prove sibling bags don't bleed into each other.
    /// </summary>
    [TestMethod]
    public async Task CaptureAtDirLevel_Expand_CaptureAtFileLevel_TemplateCombinesBoth()
    {
        var s1 = Directory.CreateDirectory(Path.Combine(_root, "Breaking Bad S01"));
        File.WriteAllText(Path.Combine(s1.FullName, "Breaking.Bad.S01E01.mkv"), "");
        File.WriteAllText(Path.Combine(s1.FullName, "Breaking.Bad.S01E02.mkv"), "");
        var s2 = Directory.CreateDirectory(Path.Combine(_root, "Fargo S02"));
        File.WriteAllText(Path.Combine(s2.FullName, "Fargo.S02E05.mkv"), "");

        var runId = await RunChain(ScanFilterJson(FsScanTarget.Directories),
            Activity(TextWorkflowKinds.TransformCapture,
                new {pattern = @"^(?<title>.+) S(?<season>\d+)$"}),
            Activity(FsWorkflowKinds.TransformExpandChildren,
                new {target = FsScanTarget.Files}),
            Activity(TextWorkflowKinds.TransformCapture,
                new {pattern = @"E(?<ep>\d+)"}),
            Activity(TextWorkflowKinds.TransformTemplate,
                new
                {
                    template = "{var:title} - S{var:season}E{var:ep:pad(2)}.{var:extension}",
                    requiredVars = new[] {"title", "season", "ep"}
                }),
            Activity(FsWorkflowKinds.ActionSaveName));

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, run.Status, run.ErrorMessage);
        // 2 directories in → 3 files out of the funnel: the 1→N step is visible in the counts.
        Assert.AreEqual(2, run.InputCount);
        Assert.AreEqual(3, run.OutputCount);

        var plan = await Plan(runId);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "Breaking Bad - S01E01.mkv",
                "Breaking Bad - S01E02.mkv",
                "Fargo - S02E05.mkv",
            },
            plan.Select(e => e.To).ToArray(),
            string.Join("; ", plan.Select(e => $"{e.From}->{e.To}")));
    }

    [TestMethod]
    public async Task CaptureMiss_Fail_RespectsSkipPolicy_AndRequiredVarsGateTheTemplate()
    {
        File.WriteAllText(Path.Combine(_root, "Show.E07.mkv"), "");
        File.WriteAllText(Path.Combine(_root, "NoEpisodeHere.mkv"), "");

        var runId = await RunChain(ScanFilterJson(FsScanTarget.Files),
            Activity(TextWorkflowKinds.TransformCapture,
                new {pattern = @"E(?<ep>\d+)", onMiss = 2 /* Fail */},
                WorkflowActivityErrorBehavior.Skip),
            Activity(TextWorkflowKinds.TransformTemplate,
                new {template = "Episode {var:ep:pad(3)}.{var:extension}", requiredVars = new[] {"ep"}}),
            Activity(FsWorkflowKinds.ActionSaveName));

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, run.Status, run.ErrorMessage);
        Assert.AreEqual(1, run.FailedItemCount, "the missed item is dropped by the Skip policy");

        var plan = await Plan(runId);
        var entry = plan.Single();
        Assert.AreEqual("Episode 007.mkv", entry.To);
    }

    [TestMethod]
    public async Task ExpandChildren_IncludeSelf_KeepsTheDirectoryInTheChain()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "Album (x)"));
        File.WriteAllText(Path.Combine(dir.FullName, "track (x).flac"), "");

        var runId = await RunChain(ScanFilterJson(FsScanTarget.Directories),
            Activity(FsWorkflowKinds.TransformExpandChildren,
                new {target = FsScanTarget.Files, includeSelf = true}),
            Activity(FsWorkflowKinds.TransformFileNameOp, new
            {
                operations = new[]
                {
                    new
                    {
                        target = 1, operation = 4, targetText = " (x)", text = "",
                    }
                }
            }),
            Activity(FsWorkflowKinds.ActionSaveName));

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, run.Status, run.ErrorMessage);

        var plan = await Plan(runId);
        CollectionAssert.AreEquivalent(
            new[] {"Album", "track.flac"},
            plan.Select(e => e.To).ToArray());
    }

    [TestMethod]
    public async Task ExpansionFromAOneToOneActivity_FailsTheRun()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "");

        var runId = await RunChain(ScanFilterJson(FsScanTarget.Files),
            Activity(LyingExpander.ActivityKind));

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, run.Status);
        StringAssert.Contains(run.ErrorMessage, "cardinality");
    }

    [TestMethod]
    public void Interpolator_Pad_OriginalText_MissingVars_AndTokenScan()
    {
        var bag = new Dictionary<string, string> {["ep"] = "7"};
        var system = new Dictionary<string, string> {["extension"] = "mkv"};

        Assert.AreEqual("E007.mkv",
            WorkflowVariableInterpolator.Interpolate("E{var:ep:pad(3)}.{var:extension}", bag, system, "x"));
        Assert.AreEqual("x!", WorkflowVariableInterpolator.Interpolate("{originalText}!", bag, system, "x"));
        Assert.AreEqual("--", WorkflowVariableInterpolator.Interpolate("-{var:missing}-", bag, system, "x"),
            "a missing non-required variable interpolates to empty");
        // A capture shadows a system variable of the same name.
        bag["extension"] = "shadowed";
        Assert.AreEqual("shadowed", WorkflowVariableInterpolator.Interpolate("{var:extension}", bag, system, "x"));

        CollectionAssert.AreEquivalent(new[] {"ep", "extension"},
            WorkflowVariableInterpolator.ReferencedVariables("{var:ep} {originalText} {var:extension} {var:ep}")
                .ToArray());
    }
}
