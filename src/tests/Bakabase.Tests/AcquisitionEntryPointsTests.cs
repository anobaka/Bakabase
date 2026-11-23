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
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Components.Workflow.Resources;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
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

    [TestMethod]
    public async Task NestedSetupDoesNotMarkCategories_AndNormalizesPreferredDrives()
    {
        var library = Path.Combine(_root, "nested-library");
        await using var scope = _sp.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>();
        var result = await setup.ApplyAsync(new AcquisitionSetupInputModel
        {
            LibraryRootDirectory = library,
            DirectoryTemplate = "游戏/{LeadKind}/{Title}",
            PreferredDriveKinds = [AcquisitionDriveKind.Unknown, AcquisitionDriveKind.Mega,
                AcquisitionDriveKind.Baidu, AcquisitionDriveKind.Mega, (AcquisitionDriveKind)999],
        });

        Assert.IsTrue(result.CreatedLibrary);
        Assert.IsNull(result.PathMarkId, "placement will mark the actual resource, not its categories");
        Assert.AreEqual(0, (await scope.ServiceProvider.GetRequiredService<IPathMarkService>().GetAll()).Count);
        var options = scope.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        Assert.AreEqual("游戏/{LeadKind}/{Title}", options.DirectoryTemplate);
        CollectionAssert.AreEqual(new[] {AcquisitionDriveKind.Mega, AcquisitionDriveKind.Baidu},
            options.PreferredDriveKinds.ToArray());
    }

    [TestMethod]
    public async Task InvalidSetupTemplateIsRejectedBeforeSettingsOrDirectoriesChange()
    {
        var library = Path.Combine(_root, "must-not-exist");
        await using var scope = _sp.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        var before = options.DirectoryTemplate;
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>().ApplyAsync(new AcquisitionSetupInputModel
            {
                LibraryRootDirectory = library,
                DirectoryTemplate = "../{Title}",
            }));

        Assert.IsFalse(Directory.Exists(library));
        Assert.AreEqual(before, options.DirectoryTemplate);
        Assert.AreEqual(0, (await scope.ServiceProvider.GetRequiredService<IPathMarkService>().GetAll()).Count);
    }

    [TestMethod]
    public async Task WorkspaceOverrideCanBeChangedAndClearedWithoutCreatingLibraryMarks()
    {
        var workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(workspace);
        await using var scope = _sp.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>();
        var result = await setup.ApplyAsync(new AcquisitionSetupInputModel
        {
            ProcessingWorkspaceDirectory = workspace,
        });
        var options = scope.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>();
        Assert.AreEqual(workspace, options.Value.ProcessingWorkspaceDirectory);
        Assert.IsFalse(result.CreatedInbox);
        Assert.IsFalse(result.CreatedLibrary);
        Assert.IsNull(result.PathMarkId);
        Assert.AreEqual(0, Directory.GetFileSystemEntries(workspace).Length);

        await setup.ApplyAsync(new AcquisitionSetupInputModel {AutoPurchaseLimit = 5});
        Assert.AreEqual(workspace, options.Value.ProcessingWorkspaceDirectory,
            "an omitted field must not reset the selected processing storage");
        await setup.ApplyAsync(new AcquisitionSetupInputModel {ProcessingWorkspaceDirectory = ""});
        Assert.IsNull(options.Value.ProcessingWorkspaceDirectory);
        Assert.IsTrue(Directory.Exists(workspace), "clearing a default never removes existing work");
        Assert.AreEqual(0, (await scope.ServiceProvider.GetRequiredService<IPathMarkService>().GetAll()).Count);
    }

    [TestMethod]
    public async Task DownloadRootCanBeOmittedOrClearedWithoutChangingProcessingOrDelivery()
    {
        var download = Path.Combine(_root, "download-work");
        var processing = Path.Combine(_root, "processing-work");
        var delivery = Path.Combine(_root, "final-output");
        Directory.CreateDirectory(download); Directory.CreateDirectory(processing);
        await using var scope = _sp.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>();
        await setup.ApplyAsync(new AcquisitionSetupInputModel
        {DownloadRootDirectory = download, ProcessingWorkspaceDirectory = processing, DeliveryRootDirectory = delivery});
        var options = scope.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>();
        await setup.ApplyAsync(new AcquisitionSetupInputModel {AutoPurchaseLimit = 7});
        Assert.AreEqual(download, options.Value.DownloadRootDirectory);
        await setup.ApplyAsync(new AcquisitionSetupInputModel {DownloadRootDirectory = ""});
        Assert.IsNull(options.Value.DownloadRootDirectory);
        Assert.AreEqual(processing, options.Value.ProcessingWorkspaceDirectory);
        Assert.AreEqual(delivery, options.Value.DeliveryRootDirectory);
        Assert.IsTrue(Directory.Exists(download));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(download).Length);
        Assert.AreEqual(0, (await scope.ServiceProvider.GetRequiredService<IPathMarkService>().GetAll()).Count);
    }

    [TestMethod]
    public async Task MissingAcquisitionDownloadRootRejectsSetupWithoutRecreatingMount()
    {
        var missing = Path.Combine(_root, "offline-volume", "downloads");
        var inbox = Path.Combine(_root, "must-not-create-download-inbox");
        await using var scope = _sp.CreateAsyncScope();
        await Assert.ThrowsExceptionAsync<DirectoryNotFoundException>(() =>
            scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>().ApplyAsync(new AcquisitionSetupInputModel
            {DownloadRootDirectory = missing, InboxDirectory = inbox}));
        Assert.IsFalse(Directory.Exists(missing)); Assert.IsFalse(Directory.Exists(inbox));
        Assert.IsNull(scope.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.DownloadRootDirectory);
    }

    [TestMethod]
    public async Task MissingWorkspaceRejectsWholeSetupWithoutRecreatingDisconnectedStorage()
    {
        var workspace = Path.Combine(_root, "unavailable-mount", "workspace");
        var inbox = Path.Combine(_root, "must-not-create-inbox");
        await using var scope = _sp.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>();
        await Assert.ThrowsExceptionAsync<DirectoryNotFoundException>(() => setup.ApplyAsync(new AcquisitionSetupInputModel
        {
            ProcessingWorkspaceDirectory = workspace,
            InboxDirectory = inbox,
        }));
        Assert.IsFalse(Directory.Exists(workspace));
        Assert.IsFalse(Directory.Exists(inbox));
        Assert.IsNull(scope.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.ProcessingWorkspaceDirectory);
    }

    [TestMethod]
    public async Task RelativeWorkspaceIsRejectedBeforeSettingsChange()
    {
        await using var scope = _sp.CreateAsyncScope();
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>().ApplyAsync(new AcquisitionSetupInputModel
            {
                ProcessingWorkspaceDirectory = "relative/workspace",
            }));
        Assert.IsNull(scope.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.ProcessingWorkspaceDirectory);
    }
    [TestMethod]
    public async Task SparseConcurrencyUpdateValidatesBeforeSavingAndPreservesOtherOptions()
    {
        await using var scope = _sp.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>();
        var manager = scope.ServiceProvider.GetRequiredService<IBOptionsManager<AcquisitionOptions>>();
        Assert.AreEqual(20, new AcquisitionOptions().Concurrency);
        await setup.ApplyAsync(new() {AutoPurchaseLimit = 17, MinimumFreeSpaceBytes = 123, PreferredDriveKinds = []});
        var previous = manager.Value with {};
        foreach (var limit in new[] {100, 1})
        {
            await setup.ApplyAsync(new() {Concurrency = limit});
            Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(previous with {Concurrency = limit}),
                System.Text.Json.JsonSerializer.Serialize(manager.Value));
        }
        foreach (var invalid in new[] {0, 101})
        {
            var inbox = Path.Combine(_root, "invalid-concurrency");
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                setup.ApplyAsync(new() {Concurrency = invalid, InboxDirectory = inbox, AutoPurchaseLimit = 20}));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                setup.ReplaceOptionsAsync(previous with {Concurrency = invalid}, default));
            Assert.IsFalse(Directory.Exists(inbox));
            Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(previous with {Concurrency = 1}),
                System.Text.Json.JsonSerializer.Serialize(manager.Value));
        }
        await setup.ApplyAsync(new() {MinimumFreeSpaceBytes = 256});
        Assert.AreEqual(1, manager.Value.Concurrency);
    }

    [TestMethod]
    public async Task RuntimeReserveCanChangeWithoutResettingSavedPathsOrCreatingResources()
    {
        await using var scope = _sp.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>();
        var options = scope.ServiceProvider.GetRequiredService<IBOptionsManager<AcquisitionOptions>>();
        Assert.AreEqual(100L * 1024 * 1024 * 1024, new AcquisitionOptions().MinimumFreeSpaceBytes);
        var processing = Path.Combine(_root, "processing");
        Directory.CreateDirectory(processing);
        await setup.ApplyAsync(new() {ProcessingWorkspaceDirectory = processing,
            DeliveryRootDirectory = Path.Combine(_root, "delivered"), MinimumFreeSpaceBytes = 0});
        await setup.ApplyAsync(new() {AutoPurchaseLimit = 8});
        Assert.AreEqual(0L, options.Value.MinimumFreeSpaceBytes, "omitting a value preserves explicit zero");
        await setup.ApplyAsync(new() {MinimumFreeSpaceBytes = 50L * 1024 * 1024 * 1024});
        Assert.AreEqual(50L * 1024 * 1024 * 1024, options.Value.MinimumFreeSpaceBytes);
        var priority = options.Value.SourceProviderPriority.ToArray();
        await setup.ApplyAsync(new() {UseEmergencySpaceReserve = true});
        Assert.IsTrue(options.Value.UseEmergencySpaceReserve);
        var oldClient = System.Text.Json.JsonSerializer.Deserialize<AcquisitionOptions>("{}")! with
            {MinimumFreeSpaceBytes = options.Value.MinimumFreeSpaceBytes, ProcessingWorkspaceDirectory = processing,
                DeliveryRootDirectory = Path.Combine(_root, "delivered"), SourceProviderPriority = priority.ToList()};
        await setup.ReplaceOptionsAsync(oldClient, default);
        Assert.IsTrue(options.Value.UseEmergencySpaceReserve);
        await setup.ApplyAsync(new() {UseEmergencySpaceReserve = false});
        Assert.IsFalse(options.Value.UseEmergencySpaceReserve);
        CollectionAssert.AreEqual(priority, options.Value.SourceProviderPriority);
        Assert.AreEqual(50L * 1024 * 1024 * 1024, options.Value.MinimumFreeSpaceBytes);
        Assert.AreEqual(processing, options.Value.ProcessingWorkspaceDirectory);
        Assert.AreEqual(Path.Combine(_root, "delivered"), options.Value.DeliveryRootDirectory);
        Assert.AreEqual(0, (await scope.ServiceProvider.GetRequiredService<IPathMarkService>().GetAll()).Count);
    }

    [TestMethod]
    [DataRow(-1L)]
    [DataRow(9_007_199_254_740_992L)]
    public async Task InvalidReserveRejectsTheEntireSetupBeforeAnyMutation(long value)
    {
        await using var scope = _sp.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>();
        var before = options.Value.MinimumFreeSpaceBytes;
        var inbox = Path.Combine(_root, "must-not-create-reserve");
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>().ApplyAsync(new()
                {MinimumFreeSpaceBytes = value, InboxDirectory = inbox, AutoPurchaseLimit = 123}));
        Assert.IsFalse(Directory.Exists(inbox));
        Assert.AreEqual(before, options.Value.MinimumFreeSpaceBytes);
        Assert.AreNotEqual(123m, options.Value.AutoPurchaseLimit);
    }

    [TestMethod]
    public async Task ModernSetupPreservesInvalidLegacyLayoutWithoutCreatingLibraryMarks()
    {
        await using var scope = _sp.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IBOptionsManager<AcquisitionOptions>>();
        var legacyRoot = Path.Combine(_root, "retained-legacy-library");
        await manager.SaveAsync(o => { o.DirectoryTemplate = "../{Title}"; o.LibraryRootDirectory = legacyRoot; });
        var setup = scope.ServiceProvider.GetRequiredService<AcquisitionSetupService>();
        var delivery = Path.Combine(_root, "modern-delivery");
        await setup.ApplyAsync(new() {DeliveryRootDirectory = delivery, MinimumFreeSpaceBytes = 0});
        Assert.AreEqual(delivery, manager.Value.DeliveryRootDirectory);
        Assert.AreEqual("../{Title}", manager.Value.DirectoryTemplate);
        Assert.AreEqual(legacyRoot, manager.Value.LibraryRootDirectory);
        Assert.IsFalse(Directory.Exists(legacyRoot));
        Assert.AreEqual(0, (await scope.ServiceProvider.GetRequiredService<IPathMarkService>().GetAll()).Count);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => setup.ApplyAsync(new() {LibraryRootDirectory = legacyRoot}));
        Assert.IsFalse(Directory.Exists(legacyRoot));
    }

}
