using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Components.Workflow;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Collection.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Collection.Tests;

/// <summary>
/// A collection meeting the acquisition pipeline. Knowing what you do not have is only half of it;
/// the other half is that the list of missing members turns into a queue without clicking each one.
/// </summary>
[TestClass]
public sealed class CollectionAcquisitionTests
{
    private IServiceProvider _sp = null!;

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

        // Something has to run when an acquisition starts. A shared-page lead resolves to the
        // recipe of this name, and these tests are about what gets queued, not about the steps.
        await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = BuiltinAcquisitionRecipes.DefaultRecipeNameFor(AcquisitionLeadKind.SharedPage),
                TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
                Enabled = true,
                Activities = [new WorkflowActivityInputModel {Kind = NoopStep.StepKind, ConfigJson = "{}"}],
            });
    }

    private ICollectionService Collections => _sp.GetRequiredService<ICollectionService>();

    private async Task<int> NewCollection(string name = "A Series") =>
        (await Collections.Add(new CollectionInputModel {Name = name})).Id;

    private async Task<int> Missing(string title) =>
        (await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(title)).ResourceId;

    private Task AddLead(int resourceId, string url) =>
        _sp.GetRequiredService<IAcquisitionLeadService>().Add(resourceId,
            new AcquisitionLeadAddInputModel {Kind = AcquisitionLeadKind.SharedPage, Value = url});

    private CollectionController Controller() => new(
        Collections,
        _sp.GetRequiredService<IResourceService>(),
        _sp.GetRequiredService<IPlaceholderResourceService>());

    private Task<Bootstrap.Models.ResponseModels.SingletonResponse<CollectionAcquireMissingResult>>
        AcquireMissing(int id) => Controller().AcquireMissing(id,
        _sp.GetRequiredService<IAcquisitionService>(),
        _sp.GetRequiredService<IAcquisitionLeadService>());

    [TestMethod]
    public async Task EverythingMissingWithSomewhereToGetItIsStarted()
    {
        var id = await NewCollection();
        var withLead = await Missing("Volume 1");
        var alsoWithLead = await Missing("Volume 2");
        var byHand = await Missing("Volume 3");

        await AddLead(withLead, "https://forum.example/thread/1");
        await AddLead(alsoWithLead, "https://forum.example/thread/2");
        await Collections.AddMembers(id, [withLead, alsoWithLead, byHand]);

        var result = (await AcquireMissing(id)).Data!;

        Assert.AreEqual(2, result.Started, string.Join(" | ", result.Problems));
        Assert.AreEqual(1, result.WithoutLead,
            "a member somebody typed in has nowhere to be got from, which is ordinary rather than a failure");

        var tasks = await _sp.GetRequiredService<IAcquisitionService>().SearchAsync();

        CollectionAssert.AreEquivalent(new[] {withLead, alsoWithLead},
            tasks.Select(t => t.ResourceId).ToArray());
        Assert.IsTrue(tasks.All(t => t.CollectionId == id),
            "each task remembers which collection asked for it");
    }

    /// <summary>
    /// Pressing it twice must not queue the same thing twice. It does not, and for the right
    /// reason: once something is on its way it has stopped being missing, so the second press has
    /// nothing to find rather than being stopped at the door.
    /// </summary>
    [TestMethod]
    public async Task AskingTwiceDoesNotQueueTheSameThingTwice()
    {
        var id = await NewCollection();
        var resourceId = await Missing("Volume 1");

        await AddLead(resourceId, "https://forum.example/thread/1");
        await Collections.AddMembers(id, [resourceId]);

        Assert.AreEqual(1, (await AcquireMissing(id)).Data!.Started);

        var second = (await AcquireMissing(id)).Data!;

        Assert.AreEqual(0, second.Started);
        Assert.AreEqual(0, second.Problems.Count, "and nothing failed — there was nothing to do");

        var progress = await Collections.GetProgress(id);

        Assert.AreEqual(1, progress.Acquiring, "the member reads as on its way, not as missing");
    }

    /// <summary>
    /// The join between the two halves: something fills a collection, and a workflow the user built
    /// takes it from there.
    /// </summary>
    [TestMethod]
    public async Task JoiningACollectionIsAnEventWorkflowsCanBuildOn()
    {
        var trigger = _sp.GetServices<IWorkflowTrigger>()
            .OfType<CollectionMembersAddedTrigger>()
            .Single();

        var payload = new CollectionMembersAddedPayload
        {
            CollectionId = 7,
            CollectionName = "A Series",
            ResourceIds = [11, 12],
            Origin = CollectionMembershipOrigin.Subscription,
            SubscriptionId = 3,
        };

        var items = trigger.ExtractItems(payload).Cast<CollectionMemberItem>().ToList();

        CollectionAssert.AreEqual(new[] {11, 12}, items.Select(i => i.ResourceId).ToArray(),
            "one item per resource, because everything downstream acts on one at a time");
        Assert.IsTrue(items.All(i => i.CollectionId == 7 && i.CollectionName == "A Series"));

        Assert.IsTrue(trigger.Matches(payload, null), "no filter matches everything");
        Assert.IsTrue(trigger.Matches(payload, """{"collectionIds":[7]}"""));
        Assert.IsFalse(trigger.Matches(payload, """{"collectionIds":[8]}"""));
        Assert.IsTrue(trigger.Matches(payload, """{"origins":[2]}"""), "brought in by a subscription");
        Assert.IsFalse(trigger.Matches(payload, """{"origins":[1]}"""), "not added by hand");
    }

    /// <summary>
    /// The item a collection emits has to be something the acquisition activity will take, or
    /// "when something joins this, go and get it" could not be drawn at all.
    /// </summary>
    [TestMethod]
    public void ACollectionMemberIsSomethingTheAcquireActivityAccepts()
    {
        var acquire = _sp.GetServices<IWorkflowActivity>()
            .Single(a => a.Kind == "action.acquisition.create");

        Assert.IsTrue(acquire.AcceptedItemInterface!.IsAssignableFrom(typeof(CollectionMemberItem)));
    }

    [TestMethod]
    public async Task AWorkflowCanFileAResourceUnderACollection()
    {
        var id = await NewCollection();
        var resourceId = await Missing("Volume 1");

        var activity = _sp.GetServices<IWorkflowActivity>()
            .Single(a => a.Kind == "action.collection.addResource");

        Assert.AreEqual(typeof(IHasResourceId), activity.AcceptedItemInterface);
        Assert.AreEqual(CollectionWorkflowKinds.ActivityGroup, activity.Group);

        await Collections.AddMembers(id, [resourceId]);

        CollectionAssert.AreEqual(new[] {resourceId},
            (await Collections.GetMembers(id)).Select(m => m.ResourceId).ToArray());
    }
}
