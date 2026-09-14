using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionRecipeSeedingTests
{
    private IServiceProvider _sp = null!;
    private BakabaseDbContext Db => _sp.GetRequiredService<BakabaseDbContext>();
    private IWorkflowDefinitionService Definitions => _sp.GetRequiredService<IWorkflowDefinitionService>();
    private Task Seed() => _sp.GetRequiredService<AcquisitionRecipeSeeder<BakabaseDbContext>>().SeedAsync();

    [TestInitialize]
    public async Task Setup() => _sp = await TestServiceBuilder.BuildServiceProvider();

    [TestMethod]
    public async Task FreshInstallationSeedsOnlyStartupRecipesExactlyOnce()
    {
        await Seed();
        await Seed();
        var actual = await Db.Set<WorkflowDefinitionDbModel>().AsNoTracking()
            .Where(d => d.IsBuiltin && d.TriggerKind == AcquisitionWorkflowKinds.TriggerRequested)
            .Select(d => d.Name).ToListAsync();
        CollectionAssert.AreEquivalent(BuiltinAcquisitionRecipes.All.Where(r => r.SeedOnStartup)
            .Select(r => r.Name).ToArray(), actual);
        Assert.IsFalse(actual.Contains(BuiltinAcquisitionRecipes.Magnet));
        Assert.IsNotNull(BuiltinAcquisitionRecipes.ByName(BuiltinAcquisitionRecipes.Magnet));
    }

    [TestMethod]
    public async Task UserDefinitionWithTheSameNameDoesNotSuppressOrBecomeTheBuiltin()
    {
        var user = await Definitions.CreateAsync(new()
        {
            Name = BuiltinAcquisitionRecipes.DirectDownload, Description = "User-owned flow",
            TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
            Activities = [new WorkflowActivityInputModel {Kind = AcquisitionStepKinds.SelectLink, Notes = "User note"}]
        });
        await Seed();
        await Seed();
        var definitions = await Db.Set<WorkflowDefinitionDbModel>().AsNoTracking()
            .Where(d => d.Name == BuiltinAcquisitionRecipes.DirectDownload &&
                        d.TriggerKind == AcquisitionWorkflowKinds.TriggerRequested).ToListAsync();
        Assert.AreEqual(2, definitions.Count);
        Assert.AreEqual(1, definitions.Count(d => d.IsBuiltin));
        var stillUser = (await Definitions.GetAsync(user.Id))!;
        Assert.IsFalse(stillUser.IsBuiltin);
        Assert.AreEqual("User-owned flow", stillUser.Description);
        Assert.AreEqual(user.Activities.Single().Id, stillUser.Activities.Single().Id);
        Assert.AreEqual(AcquisitionStepKinds.SelectLink, stillUser.Activities.Single().Kind);
        Assert.AreEqual("User note", stillUser.Activities.Single().Notes);
    }

    [TestMethod]
    public async Task DefaultSelectionPrefersBuiltinWhileExplicitNameSelectionKeepsItsExistingMeaning()
    {
        var user = await Definitions.CreateAsync(new()
        {
            Name = BuiltinAcquisitionRecipes.DirectDownload, TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
            Activities = [new WorkflowActivityInputModel {Kind = AcquisitionStepKinds.SelectLink}]
        });
        await Seed();
        var builtinId = await Db.Set<WorkflowDefinitionDbModel>().Where(d => d.IsBuiltin &&
                d.TriggerKind == AcquisitionWorkflowKinds.TriggerRequested && d.Name == BuiltinAcquisitionRecipes.DirectDownload)
            .Select(d => d.Id).SingleAsync();
        var options = _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        options.LibraryRootDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DefaultRecipe_" + Guid.NewGuid());
        var resources = _sp.GetRequiredService<IPlaceholderResourceService>();
        var acquisitions = _sp.GetRequiredService<IAcquisitionService>();
        var first = await resources.CreateByTitle("Implicit built-in default");
        var defaultTask = await acquisitions.CreateAsync(first.ResourceId, AcquisitionLeadKind.DirectUrl,
            "https://example.com/first.zip");
        Assert.AreEqual(builtinId, defaultTask.RecipeDefinitionId);

        options.RecipeByLeadKind[AcquisitionLeadKind.DirectUrl] = user.Name;
        var second = await resources.CreateByTitle("Explicit configured default");
        var configuredTask = await acquisitions.CreateAsync(second.ResourceId, AcquisitionLeadKind.DirectUrl,
            "https://example.com/second.zip");
        Assert.AreEqual(user.Id, configuredTask.RecipeDefinitionId);
    }

    [TestMethod]
    public async Task AnotherTriggersBuiltinWithSameNameDoesNotReceiveAcquisitionDocumentation()
    {
        var other = await Definitions.CreateAsync(new()
        {
            Name = BuiltinAcquisitionRecipes.DirectDownload, TriggerKind = "downloader.completed"
        });
        await Db.Set<WorkflowDefinitionDbModel>().Where(d => d.Id == other.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsBuiltin, true));
        await Seed();
        await Seed();
        var unchanged = await Db.Set<WorkflowDefinitionDbModel>().AsNoTracking().SingleAsync(d => d.Id == other.Id);
        Assert.IsNull(unchanged.Description);
        Assert.IsNull(unchanged.DescriptionKey);
        Assert.AreEqual(1, await Db.Set<WorkflowDefinitionDbModel>().CountAsync(d => d.IsBuiltin &&
            d.TriggerKind == AcquisitionWorkflowKinds.TriggerRequested && d.Name == BuiltinAcquisitionRecipes.DirectDownload));
    }

    [TestMethod]
    public async Task ExistingManualMagnetDefinitionRunAndConfiguredDefaultRemainUsable()
    {
        var oldRecipe = BuiltinAcquisitionRecipes.ByName(BuiltinAcquisitionRecipes.Magnet)!;
        var old = await Definitions.CreateAsync(new()
        {
            Name = oldRecipe.Name, TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
            Activities = oldRecipe.Steps.Select(s => new WorkflowActivityInputModel
                {Kind = s.Kind, Notes = "Existing configuration"}).ToList()
        });
        await Db.Set<WorkflowDefinitionDbModel>().Where(d => d.Id == old.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsBuiltin, true));
        var waiting = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = old.Id, Status = WorkflowRunStatus.Waiting, StartedAt = DateTime.Now,
            CurrentStepIndex = 0, CurrentItemJson = "saved cursor", WaitReason = "WaitingForFile"
        };
        Db.Set<WorkflowRunDbModel>().Add(waiting);
        await Db.SaveChangesAsync();
        var options = _sp.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        options.RecipeByLeadKind[AcquisitionLeadKind.Magnet] = oldRecipe.Name;
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LegacyMagnet_" + Guid.NewGuid());
        options.LibraryRootDirectory = System.IO.Path.Combine(directory, "library");
        options.InboxDirectory = System.IO.Path.Combine(directory, "inbox");

        await Seed();
        await Seed();

        var unchanged = (await Definitions.GetAsync(old.Id))!;
        CollectionAssert.AreEqual(old.Activities.Select(a => a.Id).ToArray(), unchanged.Activities.Select(a => a.Id).ToArray());
        CollectionAssert.AreEqual(old.Activities.Select(a => a.Kind).ToArray(), unchanged.Activities.Select(a => a.Kind).ToArray());
        Assert.IsTrue(unchanged.Activities.All(a => a.Notes == "Existing configuration"));
        var savedRun = await Db.Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync(r => r.Id == waiting.Id);
        Assert.AreEqual(WorkflowRunStatus.Waiting, savedRun.Status);
        Assert.AreEqual("saved cursor", savedRun.CurrentItemJson);
        Assert.AreEqual(0, savedRun.CurrentStepIndex);
        Assert.AreEqual(oldRecipe.Name, options.RecipeByLeadKind[AcquisitionLeadKind.Magnet]);
        var resource = await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle("Use old configured default");
        var task = await _sp.GetRequiredService<IAcquisitionService>().CreateAsync(resource.ResourceId,
            AcquisitionLeadKind.Magnet, "magnet:?xt=urn:btih:0123456789012345678901234567890123456789");
        Assert.AreEqual(old.Id, task.RecipeDefinitionId);
        Assert.IsNotNull(task.WorkflowRunId);
    }
}
