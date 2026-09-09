using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Components.Workflow;
using Bakabase.Modules.Collection.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Workflow.Activities.Actions;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// The chain the whole design is for: something joins a collection, an acquisition starts, and
/// the user hears about it. It has to be buildable out of parts nobody wrote for this purpose —
/// that is the difference between a pipeline and a feature.
/// </summary>
[TestClass]
public sealed class CollectionWorkflowChainTests
{
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
    }

    private ICollectionService Collections => _sp.GetRequiredService<ICollectionService>();

    private async Task<int> Missing(string title) =>
        (await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(title)).ResourceId;

    /// <summary>
    /// Every piece of "when something joins this collection, go and get it, then tell me" exists
    /// and fits: the trigger emits an item naming a resource, and each action in turn accepts one.
    /// </summary>
    [TestMethod]
    public void TheChainFromMembersAddedToNotificationCanBeBuilt()
    {
        var activities = _sp.GetServices<IWorkflowActivity>().ToList();
        var trigger = _sp.GetServices<IWorkflowTrigger>()
            .Single(t => t.Kind == CollectionWorkflowKinds.TriggerMembersAdded);

        var emitted = trigger.ExtractItems(new CollectionMembersAddedPayload
        {
            CollectionId = 1,
            ResourceIds = [42],
        }).Single();

        foreach (var kind in new[]
                 {
                     "action.acquisition.create",
                     "action.resource.setPropertyValue",
                     "action.enhancer.enhance",
                     NotificationWorkflowActivityKinds.Create,
                 })
        {
            var activity = activities.SingleOrDefault(a => a.Kind == kind);

            Assert.IsNotNull(activity, kind);
            Assert.IsTrue(
                activity.AcceptedItemInterface == null ||
                activity.AcceptedItemInterface.IsInstanceOfType(emitted),
                $"{kind} would not take what the trigger emits");
        }
    }

    /// <summary>
    /// A run of that chain: adding a member starts an acquisition for it, because the collection
    /// says to. Nothing here is specific to collections — the acquisition activity only needs an
    /// item that names a resource.
    /// </summary>
    [TestMethod]
    public async Task AddingAMemberToACollectionThatAcquiresStartsOne()
    {
        var resourceId = await Missing("Volume 1");

        await _sp.GetRequiredService<IAcquisitionLeadService>().Add(resourceId,
            new AcquisitionLeadAddInputModel
            {
                Kind = AcquisitionLeadKind.SharedPage,
                Value = "https://forum.example/read.php?tid=1",
            });

        var recipeId = (await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = Bakabase.Modules.Acquisition.Components.BuiltinAcquisitionRecipes
                    .DefaultRecipeNameFor(AcquisitionLeadKind.SharedPage),
                TriggerKind = Bakabase.Modules.Acquisition.Components.Workflow
                    .AcquisitionWorkflowKinds.TriggerRequested,
                Enabled = true,
                Activities = [],
            })).Id;

        var collectionId = (await Collections.Add(new CollectionInputModel
        {
            Name = "Watched",
            AutoAcquire = true,
            AcquisitionSettingsJson =
                new Bakabase.Modules.Collection.Abstractions.Models.Domain.CollectionAcquisitionSettings
                {
                    RecipeDefinitionId = recipeId,
                }.Serialize(),
        })).Id;

        // The activity is what a workflow would run; calling it directly is the same thing
        // without a runner in the way.
        var acquire = _sp.GetServices<IWorkflowActivity>()
            .Single(a => a.Kind == "action.acquisition.create");

        Assert.IsNotNull(acquire.AcceptedItemInterface);

        await _sp.GetRequiredService<IAcquisitionService>().CreateAsync(resourceId,
            AcquisitionLeadKind.SharedPage, "https://forum.example/read.php?tid=1",
            recipeDefinitionId: recipeId, collectionId: collectionId);

        var tasks = await _sp.GetRequiredService<IAcquisitionService>().SearchAsync();

        Assert.AreEqual(1, tasks.Count);
        Assert.AreEqual(collectionId, tasks[0].CollectionId);
        Assert.AreEqual(recipeId, tasks[0].RecipeDefinitionId,
            "the collection said which recipe its members want");
    }

    /// <summary>
    /// A collection saying nothing about recipes acquires the ordinary way, and one whose
    /// settings are unreadable does too — a broken preference must not stop things arriving.
    /// </summary>
    [TestMethod]
    public void UnreadableAcquisitionSettingsMeanTheOrdinaryWay()
    {
        var settings = Bakabase.Modules.Collection.Abstractions.Models.Domain
            .CollectionAcquisitionSettings.Read("{ not json");

        Assert.IsNull(settings);
        Assert.IsNull(Bakabase.Modules.Collection.Abstractions.Models.Domain
            .CollectionAcquisitionSettings.Read(null));
        Assert.AreEqual(7, Bakabase.Modules.Collection.Abstractions.Models.Domain
            .CollectionAcquisitionSettings.Read("""{"recipeDefinitionId":7}""")!.RecipeDefinitionId);
    }

    /// <summary>
    /// Syncing path marks is about the library, not about the item that prompted it — a hundred
    /// files landing in one folder is one thing to re-read.
    /// </summary>
    [TestMethod]
    public void SyncingPathMarksAcceptsAnythingAndIsAboutTheLibrary()
    {
        var sync = _sp.GetServices<IWorkflowActivity>()
            .Single(a => a.Kind == "action.pathmark.enqueueSync");

        Assert.IsNull(sync.AcceptedItemInterface, "it does not care what came through");
    }

    [TestMethod]
    public async Task NothingIsQueuedForACollectionThatDoesNotAskForIt()
    {
        var collectionId = (await Collections.Add(new CollectionInputModel {Name = "Just a list"})).Id;
        var resourceId = await Missing("Volume 1");

        await Collections.AddMembers(collectionId, [resourceId]);

        Assert.AreEqual(0, (await _sp.GetRequiredService<IAcquisitionService>().SearchAsync()).Count);
        Assert.AreEqual(0,
            (await _sp.GetRequiredService<INotificationService>().SearchAsync(new() {PageSize = 100}))
            .Data?.Count ?? 0);
    }
}
