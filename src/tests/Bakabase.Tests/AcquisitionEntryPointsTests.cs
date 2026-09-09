using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Components.Workflow.Resources;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// The ways an acquisition gets started that are not the acquisitions page: a link and nothing
/// else, a workflow deciding to go and get something, and the wizard that makes the whole thing
/// work on a fresh install.
/// </summary>
[TestClass]
public sealed class AcquisitionEntryPointsTests
{
    private IServiceProvider _sp = null!;
    private string _root = null!;

    /// <summary>A step that does nothing, so a recipe can exist without any real work happening.</summary>
    private sealed class NoopStep : IAcquisitionStep
    {
        public const string StepKind = "acquisition.test.noop";

        public string Kind => StepKind;
        public string DisplayName => "Noop (test)";
        public Type? ConfigType => null;

        public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
            AcquisitionWorkItem item, CancellationToken ct) =>
            Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Continue(item));
    }

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddAcquisitionStep<NoopStep>());
        _root = Path.Combine(Path.GetTempPath(), $"BakabaseEntry_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }

    private async Task<int> CreateRecipe() =>
        (await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = $"recipe-{Guid.NewGuid():N}",
                TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
                Enabled = true,
                Activities = [new WorkflowActivityInputModel {Kind = NoopStep.StepKind, ConfigJson = "{}"}],
            })).Id;

    /// <summary>
    /// The shortest path there is from "I found this" to "it is coming": one link, and the resource
    /// is matched or created from it. This is what the browser script calls.
    /// </summary>
    [TestMethod]
    public async Task StartingFromALinkAloneMakesTheResourceAndTheTask()
    {
        var recipeId = await CreateRecipe();
        var url = "https://soulplus.example/thread/12345";

        await using var scope = _sp.CreateAsyncScope();
        var placeholders = scope.ServiceProvider.GetRequiredService<IPlaceholderResourceService>();
        var acquisitions = scope.ServiceProvider.GetRequiredService<IAcquisitionService>();

        var placeholder = await placeholders.CreateOrMatchBySharedUrl(url);
        var task = await acquisitions.CreateAsync(placeholder.ResourceId,
            AcquisitionLeadKind.SharedPage, url, recipeDefinitionId: recipeId);

        Assert.IsTrue(placeholder.Created, "there was no resource for this link before");
        Assert.AreEqual(placeholder.ResourceId, task.ResourceId);
        Assert.AreEqual(url, task.LeadValue);

        // The same link a second time finds the resource it made rather than making another.
        var again = await placeholders.CreateOrMatchBySharedUrl(url);

        Assert.IsFalse(again.Created);
        Assert.AreEqual(placeholder.ResourceId, again.ResourceId);
    }

    /// <summary>
    /// A workflow can ask for an acquisition, which is the outer half of the arrangement: a recipe
    /// is a workflow, and a workflow can start one.
    /// </summary>
    [TestMethod]
    public async Task AWorkflowCanStartAnAcquisitionForAResourceItIsHolding()
    {
        var recipeId = await CreateRecipe();
        var resourceId = (await _sp.GetRequiredService<IPlaceholderResourceService>()
            .CreateByTitle("Something missing")).ResourceId;

        await using var scope = _sp.CreateAsyncScope();
        var lead = await scope.ServiceProvider.GetRequiredService<IAcquisitionLeadService>().Add(
            resourceId, new Modules.Acquisition.Models.Input.AcquisitionLeadAddInputModel
            {
                Kind = AcquisitionLeadKind.SharedPage,
                Value = "https://example.com/thread/9",
                Origin = AcquisitionLeadOrigin.User,
            });

        Assert.IsNotNull(lead.Lead);

        var activity = new AcquisitionCreateActivity();
        var ctx = new WorkflowExecutionContext
        {
            RunId = 1,
            WorkflowDefinitionId = 1,
            TriggerKind = "test",
            Payload = new object(),
            ActivityConfigJson = JsonConvert.SerializeObject(new {recipeDefinitionId = recipeId}),
            Services = scope.ServiceProvider,
            Logger = NullLogger.Instance,
        };

        var outcome = await activity.ProcessItemAsync(ctx,
            new ResourceWorkflowItem {Id = resourceId, Name = "Something missing"},
            CancellationToken.None);

        Assert.IsTrue(outcome.Keep);

        var tasks = await scope.ServiceProvider.GetRequiredService<IAcquisitionService>()
            .SearchAsync(resourceId: resourceId);

        Assert.AreEqual(1, tasks.Count);
        Assert.AreEqual(recipeId, tasks[0].RecipeDefinitionId);
    }

    /// <summary>
    /// A resource with nowhere to be got from is dropped rather than failing the run: a chain
    /// running over a list will hit plenty of those.
    /// </summary>
    [TestMethod]
    public async Task AResourceWithNoLeadIsDroppedRatherThanFailingTheRun()
    {
        var resourceId = (await _sp.GetRequiredService<IPlaceholderResourceService>()
            .CreateByTitle("Nowhere to get this")).ResourceId;

        await using var scope = _sp.CreateAsyncScope();
        var outcome = await new AcquisitionCreateActivity().ProcessItemAsync(
            new WorkflowExecutionContext
            {
                RunId = 1,
                WorkflowDefinitionId = 1,
                TriggerKind = "test",
                Payload = new object(),
                ActivityConfigJson = "{}",
                Services = scope.ServiceProvider,
                Logger = NullLogger.Instance,
            },
            new ResourceWorkflowItem {Id = resourceId},
            CancellationToken.None);

        Assert.IsFalse(outcome.Keep);
    }

    /// <summary>
    /// The wizard's whole job. A library folder means nothing to Bakabase until something says
    /// "each folder in here is a resource" — and that is the one path mark a user of this pipeline
    /// should never have to learn about.
    /// </summary>
    [TestMethod]
    public async Task TheWizardWritesTheSettingsAndMarksTheLibrary()
    {
        var inbox = Path.Combine(_root, "inbox");
        var library = Path.Combine(_root, "library");

        await using var scope = _sp.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>();

        var result = await setup.ApplyAsync(new AcquisitionSetupInputModel
        {
            InboxDirectory = inbox,
            LibraryRootDirectory = library,
            DirectoryTemplate = "{Title}",
            AutoPurchaseLimit = 5m,
        });

        Assert.IsTrue(result.CreatedInbox, "the folders are made rather than merely recorded");
        Assert.IsTrue(result.CreatedLibrary);
        Assert.IsTrue(Directory.Exists(inbox));
        Assert.IsNotNull(result.PathMarkId);

        var marks = await scope.ServiceProvider.GetRequiredService<IPathMarkService>().GetAll();
        var mark = marks.Single(m => m.Id == result.PathMarkId);

        Assert.AreEqual(PathMarkType.Resource, mark.Type);

        var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson)!;

        Assert.AreEqual(PathMatchMode.Layer, config.MatchMode);
        Assert.AreEqual(1, config.Layer);
        Assert.AreEqual(PathFilterFsType.Directory, config.FsTypeFilter);

        // Running it again does not make a second mark for the same folder.
        var second = await setup.ApplyAsync(new AcquisitionSetupInputModel
        {
            LibraryRootDirectory = library,
        });

        Assert.AreEqual(result.PathMarkId, second.PathMarkId);
        Assert.IsFalse(second.CreatedLibrary);
    }
}
