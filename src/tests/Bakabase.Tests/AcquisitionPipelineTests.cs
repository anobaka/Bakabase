using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// The seam between the two halves of getting a resource: steps that know nothing about workflows,
/// and a workflow engine that knows nothing about acquisition. What is under test is that a task
/// row, a run and the steps in between never disagree about what is happening.
/// </summary>
[TestClass]
public sealed class AcquisitionPipelineTests
{
    private IServiceProvider _sp = null!;

    /// <summary>Continues, leaving a mark so a test can see it ran.</summary>
    private sealed class FetchStep : IAcquisitionStep
    {
        public const string StepKind = "acquisition.test.fetch";
        public static int Executions;

        public string Kind => StepKind;
        public string DisplayName => "Fetch (test)";
        public Type? ConfigType => null;

        public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, CancellationToken ct)
        {
            Executions++;

            return Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Continue(
                item with {Files = [.. item.Files, "fetched.zip"]}));
        }
    }

    /// <summary>Waits for a password, then records it and moves on.</summary>
    private sealed class AskStep : IAcquisitionStep
    {
        public const string StepKind = "acquisition.test.ask";

        public string Kind => StepKind;
        public string DisplayName => "Ask (test)";
        public Type? ConfigType => null;

        public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, CancellationToken ct) =>
            Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Suspend(
                AcquisitionWaitReason.PasswordUnknown, """{"ask":"password"}""", item));

        public Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, AcquisitionResumeSignal signal, CancellationToken ct)
        {
            var vars = new Dictionary<string, string>(item.Variables)
            {
                ["password"] = signal.PayloadJson?.Trim('"') ?? ""
            };

            return Task.FromResult<AcquisitionStepOutcome>(
                new AcquisitionStepOutcome.Continue(item with {Variables = vars}));
        }
    }

    /// <summary>Stands in for placement: says where the files ended up.</summary>
    private sealed class PlaceStep : IAcquisitionStep
    {
        public const string StepKind = "acquisition.test.place";

        public string Kind => StepKind;
        public string DisplayName => "Place (test)";
        public Type? ConfigType => null;

        public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, CancellationToken ct) =>
            Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Continue(
                item with {TargetDirectory = $"/library/{item.WorkingName}"}));
    }

    [TestInitialize]
    public async Task Setup()
    {
        FetchStep.Executions = 0;
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddAcquisitionStep<FetchStep>();
            services.AddAcquisitionStep<AskStep>();
            services.AddAcquisitionStep<PlaceStep>();
        });
    }

    private static BTaskArgs BuildArgs(IServiceProvider sp) => new(
        new PauseToken(), CancellationToken.None, new BTask("test", () => "test"),
        _ => Task.CompletedTask, sp);

    private async Task<int> CreateRecipe(params string[] stepKinds) =>
        (await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = $"recipe-{Guid.NewGuid():N}",
                TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
                Enabled = true,
                Activities = stepKinds.Select(k => new WorkflowActivityInputModel
                {
                    Kind = k,
                    ConfigJson = "{}",
                    OnItemError = WorkflowActivityErrorBehavior.Fail,
                }).ToList()
            })).Id;

    private async Task<int> CreateMissingResource(string name = "Something I do not have")
    {
        var result = await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(name);

        return result.ResourceId;
    }

    /// <summary>Drives the run the way the background task would.</summary>
    private async Task Execute(int runId)
    {
        WorkflowRunner<BakabaseDbContext> runner;
        await using (var scope = _sp.CreateAsyncScope())
        {
            runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner<BakabaseDbContext>>();
        }

        await runner.ExecuteAsync(runId, BuildArgs(_sp));

        // The daemon is not running in tests, so the handler RunManuallyAsync queued would sit in
        // the map forever and refuse to be replaced when the run is resumed. Dropping it here is
        // what the daemon does once it has actually executed the task.
        await _sp.GetRequiredService<BTaskManager>().Clean($"workflow.run.{runId}");
    }

    private async Task<AcquisitionTask> Task_(int taskId)
    {
        await using var scope = _sp.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<IAcquisitionService>().GetAsync(taskId))!;
    }

    private async Task<WorkflowRunDbModel> Run(int runId)
    {
        await using var scope = _sp.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
            .Set<WorkflowRunDbModel>().AsNoTracking().FirstAsync(r => r.Id == runId);
    }

    private async Task Reconcile()
    {
        await using var scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAcquisitionQueue>().ReconcileAsync();
    }

    private async Task Pump()
    {
        await using var scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAcquisitionQueue>().PumpAsync();
    }

    private async Task<AcquisitionTask> Create(int resourceId, int recipeId)
    {
        await using var scope = _sp.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IAcquisitionService>()
            .CreateAsync(resourceId, AcquisitionLeadKind.SharedPage, "https://example.com/thread/1",
                recipeDefinitionId: recipeId);
    }

    /// <summary>
    /// The whole vertical, on fake steps: asking for something starts a run, the run stops for a
    /// password, answering it carries on, and the task row says the same thing as the run at every
    /// point along the way.
    /// </summary>
    [TestMethod]
    public async Task Create_RunToASuspension_Resume_Complete_AndTheTaskRowAgreesThroughout()
    {
        var recipeId = await CreateRecipe(FetchStep.StepKind, AskStep.StepKind, PlaceStep.StepKind);
        var resourceId = await CreateMissingResource();

        var created = await Create(resourceId, recipeId);

        Assert.AreEqual(AcquisitionStatus.Running, created.Status, "there was room, so it started");
        Assert.IsNotNull(created.WorkflowRunId);
        Assert.AreEqual(resourceId, created.ResourceId);

        var runId = created.WorkflowRunId!.Value;
        await Execute(runId);

        var waiting = await Task_(created.Id);
        Assert.AreEqual(AcquisitionStatus.Waiting, waiting.Status);
        Assert.AreEqual(AcquisitionWaitReason.PasswordUnknown, waiting.WaitReason);
        Assert.AreEqual("""{"ask":"password"}""", waiting.WaitPromptJson);
        Assert.AreEqual(1, waiting.CurrentStepIndex, "the run is parked on the step that asked");
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await Run(runId)).Status);
        Assert.AreEqual(1, FetchStep.Executions);

        await using (var scope = _sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAcquisitionService>().ResumeAsync(
                created.Id,
                System.Text.Json.JsonSerializer.Serialize(new AcquisitionResumeSignal(
                    AcquisitionWaitReason.PasswordUnknown, "\"hunter2\"")));
        }

        await Execute(runId);

        Assert.AreEqual(WorkflowRunStatus.Success, (await Run(runId)).Status);
        Assert.AreEqual(1, FetchStep.Executions, "a resumed run does not re-run the steps before the cursor");

        var done = await Task_(created.Id);
        Assert.AreEqual("/library/Something I do not have", done.TargetDirectory,
            "what the steps learned reaches the task row");

        // Nothing runs after a recipe's last step, so only the queue tick can notice it finished.
        Assert.AreNotEqual(AcquisitionStatus.Completed, done.Status);
        await Reconcile();
        Assert.AreEqual(AcquisitionStatus.Completed, (await Task_(created.Id)).Status);
        Assert.IsNotNull((await Task_(created.Id)).CompletedAt);
    }

    /// <summary>
    /// The limit is about work in flight, so a queued task waits its turn and starts when one
    /// finishes — without anyone asking again.
    /// </summary>
    [TestMethod]
    public async Task TheConcurrencyLimitQueuesTheSecondTask_AndTheTickStartsItLater()
    {
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.Concurrency = 1;

        var recipeId = await CreateRecipe(FetchStep.StepKind, PlaceStep.StepKind);
        var first = await Create(await CreateMissingResource("First"), recipeId);
        var second = await Create(await CreateMissingResource("Second"), recipeId);

        Assert.AreEqual(AcquisitionStatus.Running, first.Status);
        Assert.AreEqual(AcquisitionStatus.Pending, second.Status, "one at a time");
        Assert.IsNull(second.WorkflowRunId, "a queued task has not started a run at all");

        // Still no room, so the tick changes nothing.
        await Pump();
        Assert.IsNull((await Task_(second.Id)).WorkflowRunId);

        await Execute(first.WorkflowRunId!.Value);
        await Reconcile();
        Assert.AreEqual(AcquisitionStatus.Completed, (await Task_(first.Id)).Status);

        await Pump();
        var started = await Task_(second.Id);
        Assert.AreEqual(AcquisitionStatus.Running, started.Status);
        Assert.IsNotNull(started.WorkflowRunId);
    }

    /// <summary>
    /// A task waiting on a person may wait for days. Letting it hold a slot would turn the limit
    /// into "how many things can be half-done", which protects nothing.
    /// </summary>
    [TestMethod]
    public async Task AWaitingTaskDoesNotHoldASlot()
    {
        _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.Concurrency = 1;

        var waitingRecipe = await CreateRecipe(AskStep.StepKind, PlaceStep.StepKind);
        var first = await Create(await CreateMissingResource("Waits"), waitingRecipe);
        await Execute(first.WorkflowRunId!.Value);
        Assert.AreEqual(AcquisitionStatus.Waiting, (await Task_(first.Id)).Status);

        var second = await Create(await CreateMissingResource("Runs"), waitingRecipe);

        Assert.AreEqual(AcquisitionStatus.Running, second.Status,
            "the slot the waiting task was using is free");
    }

    [TestMethod]
    public async Task AResourceThatAlreadyHasFiles_OrIsAlreadyBeingAcquired_IsRefused()
    {
        var recipeId = await CreateRecipe(FetchStep.StepKind);
        var resourceId = await CreateMissingResource();

        await Create(resourceId, recipeId);

        var again = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => Create(resourceId, recipeId));
        StringAssert.Contains(again.Message, "already being acquired");

        // And once it has files there is nothing left to get.
        var withFiles = await CreateMissingResource("Has files");
        await using (var scope = _sp.CreateAsyncScope())
        {
            var resources = scope.ServiceProvider.GetRequiredService<IResourceService>();
            var resource = (await resources.Get(withFiles))!;

            resource.Path = "/somewhere/real";
            await resources.AddOrPutRange([resource]);
        }

        var refused = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => Create(withFiles, recipeId));
        StringAssert.Contains(refused.Message, "already has local files");
    }

    /// <summary>
    /// A failed run keeps its cursor, so retrying re-runs the step that stopped rather than the
    /// chain — which is the whole reason steps are required to be idempotent rather than the run.
    /// </summary>
    [TestMethod]
    public async Task Retry_PicksUpAtTheStepThatStopped()
    {
        var recipeId = await CreateRecipe(FetchStep.StepKind, AskStep.StepKind);
        var task = await Create(await CreateMissingResource(), recipeId);
        var runId = task.WorkflowRunId!.Value;

        await Execute(runId);
        Assert.AreEqual(AcquisitionStatus.Waiting, (await Task_(task.Id)).Status);

        await using (var scope = _sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAcquisitionService>().CancelAsync(task.Id);
        }

        Assert.AreEqual(AcquisitionStatus.Cancelled, (await Task_(task.Id)).Status);

        await using (var scope = _sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAcquisitionService>().RetryAsync(task.Id);
        }

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Pending, run.Status);
        Assert.AreEqual(1, run.CurrentStepIndex, "the cursor survived the cancel and the retry");
        Assert.AreEqual(1, FetchStep.Executions, "and the fetch is not repeated");
    }

    /// <summary>
    /// Recipes are seeded by name, once. A recipe naming a step this build does not have is skipped
    /// rather than created broken — the steps land over several releases.
    /// </summary>
    [TestMethod]
    public async Task TheSeeder_IsIdempotent_AndSkipsRecipesWhoseStepsAreMissing()
    {
        await using (var scope = _sp.CreateAsyncScope())
        {
            var seeder = scope.ServiceProvider
                .GetRequiredService<AcquisitionRecipeSeeder<BakabaseDbContext>>();
            await seeder.SeedAsync();
            await seeder.SeedAsync();
        }

        await using var read = _sp.CreateAsyncScope();
        var defs = await read.ServiceProvider.GetRequiredService<BakabaseDbContext>()
            .Set<WorkflowDefinitionDbModel>().AsNoTracking()
            .Where(d => d.TriggerKind == AcquisitionWorkflowKinds.TriggerRequested)
            .ToListAsync();

        // None of the real steps exist in this build yet, so nothing seeds — and seeding twice
        // still produces nothing rather than duplicates.
        Assert.AreEqual(0, defs.Count(d => d.IsBuiltin),
            "no built-in recipe can be seeded while its steps are unimplemented");
    }

    /// <summary>
    /// A built-in recipe is a seed, not a document: a later release adds a step to it, and that must
    /// not fight a user's edits. Switching one off is still theirs.
    /// </summary>
    [TestMethod]
    public async Task ABuiltinRecipeCannotBeEditedOrDeleted_ButCanBeSwitchedOff()
    {
        var id = await CreateRecipe(FetchStep.StepKind);
        await using (var scope = _sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
                .Set<WorkflowDefinitionDbModel>().Where(d => d.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsBuiltin, _ => true));
        }

        // A fresh scope: the definition created above is still tracked in the one that made it,
        // and a tracked entity would not show the flag that was just written around it.
        await using var read = _sp.CreateAsyncScope();
        var workflows = read.ServiceProvider.GetRequiredService<IWorkflowDefinitionService>();

        var edit = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            workflows.UpdateAsync(id, new WorkflowDefinitionUpdateInputModel {Name = "mine now"}));
        StringAssert.Contains(edit.Message, "Copy it");

        var delete = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => workflows.DeleteAsync(id));
        StringAssert.Contains(delete.Message, "cannot be deleted");

        await workflows.UpdateAsync(id, new WorkflowDefinitionUpdateInputModel {Enabled = false});
        Assert.IsFalse((await workflows.GetAsync(id))!.Enabled);
    }

    /// <summary>
    /// A recipe is an ordinary workflow, so the editor's chain typing applies to it: a step of a
    /// different domain cannot be dropped into one.
    /// </summary>
    [TestMethod]
    public async Task ARecipeRefusesActivitiesFromAnotherDomain()
    {
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
                new WorkflowDefinitionCreationInputModel
                {
                    Name = $"bad-{Guid.NewGuid():N}",
                    TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
                    Activities =
                    [
                        new WorkflowActivityInputModel
                        {
                            Kind = Bakabase.Service.Components.Workflow.Fs.FsWorkflowKinds.ActionSaveName,
                            ConfigJson = "{}",
                        }
                    ]
                }));

        StringAssert.Contains(ex.Message, AcquisitionWorkflowKinds.ItemAcquisition);
    }

    /// <summary>
    /// The item is an <c>ITextWorkpiece</c>, so the text activities that already exist can clean up
    /// the folder name a recipe is about to use — without acquisition knowing they exist.
    /// </summary>
    [TestMethod]
    public async Task ATextTransformCanRewriteTheDirectoryNameMidRecipe()
    {
        var recipeId = (await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = $"recipe-{Guid.NewGuid():N}",
                TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
                Enabled = true,
                Activities =
                [
                    new WorkflowActivityInputModel {Kind = FetchStep.StepKind, ConfigJson = "{}"},
                    new WorkflowActivityInputModel
                    {
                        Kind = Bakabase.Service.Components.Workflow.Text.TextWorkflowKinds.TransformTemplate,
                        ConfigJson = """{"template":"A Work"}""",
                    },
                    new WorkflowActivityInputModel {Kind = PlaceStep.StepKind, ConfigJson = "{}"},
                ]
            })).Id;

        var resourceId = await CreateMissingResource("[Group] A Work");
        var task = await Create(resourceId, recipeId);
        await Execute(task.WorkflowRunId!.Value);

        Assert.AreEqual("/library/A Work", (await Task_(task.Id)).TargetDirectory,
            "the text transform rewrote the name the placement step then used");
    }
}
