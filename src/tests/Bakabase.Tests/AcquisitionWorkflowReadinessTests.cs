using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Steps;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.Steps;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionWorkflowReadinessTests
{
    private IServiceProvider _sp = null!;
    private string _root = null!;
    private readonly TestPlatform _platform = new();
    private IAcquisitionService Acquisitions => _sp.GetRequiredService<IAcquisitionService>();
    private IWorkflowDefinitionService Workflows => _sp.GetRequiredService<IWorkflowDefinitionService>();
    private BakabaseDbContext Db => _sp.GetRequiredService<BakabaseDbContext>();
    private AcquisitionOptions Options => _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;

    private sealed class DeclaredSource : IAcquisitionStep
    {
        public const string StepKind = "test.source.accepting-torrent";
        public string Kind => StepKind;
        public string DisplayName => "A user-named source";
        public Type? ConfigType => null;
        public IReadOnlyList<AcquisitionLeadKind>? AcceptedLeadKinds => [AcquisitionLeadKind.Torrent];
        public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext context, AcquisitionWorkItem item, CancellationToken ct) =>
            throw new AssertFailedException("Reading workflow metadata must not execute a source.");
    }

    private sealed class TestPlatform : IPlatformConnector
    {
        public string Directory { get; set; } = "";
        public bool AlreadyLocal { get; set; }
        public int Fetches { get; private set; }
        public ResourceSource Source => ResourceSource.Steam;
        public bool CanFetch => true;
        public Task<IReadOnlyList<PlatformHolding>> EnumerateHoldingsAsync(CancellationToken ct) => throw new AssertFailedException("No external inventory calls.");
        public Task<string?> DetectLocalPathAsync(string key, CancellationToken ct) => Task.FromResult(AlreadyLocal ? Directory : null);
        public Task<PlatformFetchOutcome> FetchAsync(string key, string work, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Fetches++;
            return Task.FromResult<PlatformFetchOutcome>(new PlatformFetchOutcome.Done(Directory));
        }
    }

    private sealed class PlatformRegistry(TestPlatform platform) : IPlatformConnectorRegistry
    {
        public IReadOnlyCollection<ResourceSource> Sources => [ResourceSource.Steam];
        public IPlatformConnector? Get(ResourceSource source) => source == ResourceSource.Steam ? platform : null;
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "BakabaseReadiness_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddAcquisitionStep<DeclaredSource>();
            services.AddSingleton<IPlatformConnectorRegistry>(new PlatformRegistry(_platform));
        });
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private Task Seed() => _sp.GetRequiredService<AcquisitionRecipeSeeder<BakabaseDbContext>>().SeedAsync();
    private async Task<int> Missing() => (await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle("Readiness sample")).ResourceId;

    [TestMethod]
    public async Task SummariesExposeSavedDescriptionsAndDeclaredInputKinds_WithoutGuessingFromNames()
    {
        var custom = await Workflows.CreateAsync(new WorkflowDefinitionCreationInputModel
        {
            Name = "Not a built-in download name", Description = "The user's exact description",
            TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
            Activities = [new() { Kind = DeclaredSource.StepKind }, new() { Kind = AcquisitionStepKinds.Materialize }],
        });
        var summary = (await Acquisitions.GetRecipesAsync()).Single(r => r.DefinitionId == custom.Id);
        Assert.AreEqual("The user's exact description", summary.Description);
        Assert.IsNull(summary.DescriptionKey);
        Assert.IsFalse(summary.IsBuiltin);
        CollectionAssert.AreEqual(new[] { AcquisitionLeadKind.Torrent }, summary.ApplicableLeadKinds);
        Assert.IsTrue(summary.Validation.IsValid);
        var withoutMaterialization = await Workflows.CreateAsync(new WorkflowDefinitionCreationInputModel
        {
            Name = "Source only", TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
            Activities = [new() { Kind = DeclaredSource.StepKind }],
        });
        Assert.AreEqual(0, (await Acquisitions.GetRecipesAsync()).Single(r => r.DefinitionId == withoutMaterialization.Id).ApplicableLeadKinds.Count);
    }

    [TestMethod]
    public async Task MissingLibrary_IsReportedBeforeCreatingTaskOrRun_WhileWorkflowStillSaves()
    {
        await Seed();
        Options.LibraryRootDirectory = null;
        var recipe = (await Acquisitions.GetRecipesAsync()).Single(r => r.Name == BuiltinAcquisitionRecipes.DirectDownload);
        Assert.IsFalse(recipe.Validation.IsValid);
        Assert.IsTrue(recipe.Validation.Diagnostics.Any(d => d.Code == "acquisition.library.missing"));
        var resourceId = await Missing();
        await Assert.ThrowsExceptionAsync<WorkflowValidationException>(() => Acquisitions.CreateAsync(resourceId,
            AcquisitionLeadKind.DirectUrl, "https://example.invalid/resource.zip", recipeDefinitionId: recipe.DefinitionId));
        Assert.AreEqual(0, await Db.Set<AcquisitionTaskDbModel>().CountAsync());
        Assert.AreEqual(0, await Db.Set<WorkflowRunDbModel>().CountAsync());
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "library")));
    }

    [TestMethod]
    public async Task MissingAi_IsReportedBeforeCreatingTaskOrRun_WithoutRequiringItToSaveAWorkflow()
    {
        Options.LibraryRootDirectory = Path.Combine(_root, "library");
        Options.InboxDirectory = Path.Combine(_root, "inbox");
        await _sp.GetRequiredService<IAiFeatureService>().DeleteConfigAsync(AiFeature.PostParser);
        await _sp.GetRequiredService<IAiFeatureService>().DeleteConfigAsync(AiFeature.Default);
        await Seed();
        var recipe = (await Acquisitions.GetRecipesAsync()).Single(r => r.Name == BuiltinAcquisitionRecipes.ForumPostWithCloudDrive);
        Assert.IsFalse(recipe.Validation.IsValid);
        Assert.IsTrue(recipe.Validation.Diagnostics.Any(d => d.Code == "acquisition.ai.missing"));
        var resourceId = await Missing();
        await Assert.ThrowsExceptionAsync<WorkflowValidationException>(() => Acquisitions.CreateAsync(resourceId,
            AcquisitionLeadKind.SharedPage, "https://example.invalid/post", recipeDefinitionId: recipe.DefinitionId));
        Assert.AreEqual(0, await Db.Set<AcquisitionTaskDbModel>().CountAsync());
        Assert.AreEqual(0, await Db.Set<WorkflowRunDbModel>().CountAsync());
        Assert.IsFalse(Directory.Exists(Options.LibraryRootDirectory));
        Assert.IsFalse(Directory.Exists(Options.InboxDirectory));
    }

    [TestMethod]
    public async Task SeederAddsAutomaticMagnetAndTorrent_WithoutReplacingTheExistingManualChain()
    {
        var oldKinds = new[] { AcquisitionStepKinds.WaitForInbox, AcquisitionStepKinds.Place, AcquisitionStepKinds.Materialize };
        var old = await Workflows.CreateAsync(new WorkflowDefinitionCreationInputModel
        {
            Name = BuiltinAcquisitionRecipes.Magnet, TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
            Activities = oldKinds.Select(kind => new WorkflowActivityInputModel { Kind = kind, Notes = "Existing note" }).ToList(),
        });
        await Db.Set<WorkflowDefinitionDbModel>().Where(d => d.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.IsBuiltin, true));
        var oldIds = old.Activities.Select(a => a.Id).ToArray();
        await Seed();
        await Seed();
        var manual = (await Workflows.GetAsync(old.Id))!;
        CollectionAssert.AreEqual(oldKinds, manual.Activities.Select(a => a.Kind).ToArray());
        CollectionAssert.AreEqual(oldIds, manual.Activities.Select(a => a.Id).ToArray());
        Assert.IsTrue(manual.Activities.All(a => a.Notes == "Existing note"));
        Assert.AreEqual("acquisition.workflow.manualMagnet.description", manual.DescriptionKey);
        var recipes = await Acquisitions.GetRecipesAsync();
        var magnet = recipes.Single(r => r.Name == BuiltinAcquisitionRecipes.MagnetDownload);
        var torrent = recipes.Single(r => r.Name == BuiltinAcquisitionRecipes.TorrentDownload);
        Assert.IsTrue(magnet.IsBuiltin && torrent.IsBuiltin);
        Assert.AreEqual(AcquisitionStepKinds.FetchMagnet, magnet.StepKinds[0]);
        Assert.AreEqual(AcquisitionStepKinds.FetchTorrent, torrent.StepKinds[0]);
        CollectionAssert.AreEqual(new[] { AcquisitionLeadKind.Magnet }, magnet.ApplicableLeadKinds);
        CollectionAssert.AreEqual(new[] { AcquisitionLeadKind.Torrent }, torrent.ApplicableLeadKinds);
        Assert.AreEqual(BuiltinAcquisitionRecipes.MagnetDownload, BuiltinAcquisitionRecipes.DefaultRecipeNameFor(AcquisitionLeadKind.Magnet));
        Assert.AreEqual(BuiltinAcquisitionRecipes.TorrentDownload, BuiltinAcquisitionRecipes.DefaultRecipeNameFor(AcquisitionLeadKind.Torrent));
    }

    [DataTestMethod]
    [DataRow("existing")]
    [DataRow("downloaded")]
    [DataRow("resumed")]
    public async Task PlatformArrivalProvidesTheDirectoryMaterializationNeeds_WithoutMovingTheInstallation(string mode)
    {
        _platform.Directory = Path.Combine(_root, "platform-installation");
        _platform.AlreadyLocal = mode != "downloaded";
        Directory.CreateDirectory(_platform.Directory);
        var original = Path.Combine(_platform.Directory, "game.dat");
        await File.WriteAllTextAsync(original, "Platform-owned file");
        var resourceId = await Missing();
        var working = Path.Combine(_root, "work");
        Directory.CreateDirectory(working);
        var ctx = new AcquisitionStepContext(_sp, NullLogger.Instance, (_, _) => Task.CompletedTask, working,
            "{\"notify\":false,\"cleanWorkingDirectory\":false}");
        var item = new AcquisitionWorkItem { ResourceId = resourceId, LeadKind = AcquisitionLeadKind.PlatformHolding, LeadValue = "Steam:123" };
        var step = new FetchFromPlatformStep();
        var result = mode == "resumed"
            ? await step.ResumeAsync(ctx, item, new AcquisitionResumeSignal(AcquisitionWaitReason.PlatformFetch, null), default)
            : await step.ExecuteAsync(ctx, item, default);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(result);
        var arrived = ((AcquisitionStepOutcome.Continue) result).Item;
        Assert.AreEqual(_platform.Directory, arrived.ExtractedDirectory);
        Assert.AreEqual(_platform.Directory, arrived.TargetDirectory);
        var materialized = await new MaterializeStep().ExecuteAsync(ctx, arrived, default);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(materialized);
        Assert.AreEqual(_platform.Directory, (await _sp.GetRequiredService<IResourceService>().GetAll(r => r.Id == resourceId)).Single().Path);
        Assert.AreEqual("Platform-owned file", await File.ReadAllTextAsync(original));
        Assert.AreEqual(mode == "downloaded" ? 1 : 0, _platform.Fetches);
    }

    [TestMethod]
    public async Task TorrentPlacementPreservesTheSingleInternalFolderAndNestedFiles_ThenBindsItsLibraryRoot()
    {
        Options.LibraryRootDirectory = Path.Combine(_root, "library");
        var working = Path.Combine(_root, "work");
        var source = Path.Combine(working, "torrent-data");
        var relative = new[] { "Disc1/Content/readme.txt", "Disc1/Content/locale/zh.txt" };
        foreach (var path in relative)
        {
            var file = Path.Combine(source, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, path);
        }
        var resourceId = await Missing();
        var ctx = new AcquisitionStepContext(_sp, NullLogger.Instance, (_, _) => Task.CompletedTask, working,
            "{\"notify\":false,\"cleanWorkingDirectory\":false}");
        var item = new AcquisitionWorkItem
        {
            ResourceId = resourceId, LeadKind = AcquisitionLeadKind.Torrent, LeadValue = "https://example.invalid/file.torrent",
            Title = "Torrent work", WorkingDirectory = working, ExtractedDirectory = source,
            Files = relative.Select(path => Path.Combine(source, path)).ToList(), PreserveDirectoryStructure = true,
        };
        var placement = await new PlaceStep().ExecuteAsync(ctx, item, default);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(placement);
        var placed = ((AcquisitionStepOutcome.Continue) placement).Item;
        var expected = Path.Combine(Options.LibraryRootDirectory, "Torrent work");
        Assert.AreEqual(expected, placed.TargetDirectory);
        foreach (var path in relative)
            Assert.AreEqual(path, await File.ReadAllTextAsync(Path.Combine(expected, path)));
        Assert.IsFalse(File.Exists(Path.Combine(expected, "readme.txt")), "The real Disc1/Content tree must not collapse.");
        var materialized = await new MaterializeStep().ExecuteAsync(ctx, placed, default);
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(materialized);
        Assert.AreEqual(expected, (await _sp.GetRequiredService<IResourceService>().GetAll(r => r.Id == resourceId)).Single().Path);
    }

    [TestMethod]
    public async Task QueuedTaskWhoseConfigurationBecomesInvalid_FailsWithoutCreatingARun()
    {
        await Seed();
        Options.LibraryRootDirectory = Path.Combine(_root, "library");
        Options.Concurrency = 1;
        var recipe = (await Acquisitions.GetRecipesAsync()).Single(r => r.Name == BuiltinAcquisitionRecipes.DirectDownload);
        var occupying = new AcquisitionTaskDbModel
        {
            ResourceId = await Missing(), RecipeDefinitionId = recipe.DefinitionId,
            Status = AcquisitionStatus.Running, LeadKind = AcquisitionLeadKind.DirectUrl,
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now,
        };
        Db.Set<AcquisitionTaskDbModel>().Add(occupying);
        await Db.SaveChangesAsync();
        var target = await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle("Queued resource");
        var queued = await Acquisitions.CreateAsync(target.ResourceId, AcquisitionLeadKind.DirectUrl,
            "https://example.invalid/resource.zip", recipeDefinitionId: recipe.DefinitionId);
        Assert.AreEqual(AcquisitionStatus.Pending, queued.Status);
        Assert.IsNull(queued.WorkflowRunId);
        Options.LibraryRootDirectory = null;
        occupying.Status = AcquisitionStatus.Completed;
        await Db.SaveChangesAsync();
        await _sp.GetRequiredService<IAcquisitionQueue>().PumpAsync();
        var failed = (await Acquisitions.GetAsync(queued.Id))!;
        Assert.AreEqual(AcquisitionStatus.Failed, failed.Status);
        Assert.IsNull(failed.WorkflowRunId);
        Assert.IsTrue(!string.IsNullOrWhiteSpace(failed.Error));
        Assert.AreEqual(0, await Db.Set<WorkflowRunDbModel>().CountAsync());
    }
}
