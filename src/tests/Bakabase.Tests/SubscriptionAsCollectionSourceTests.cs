using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Subscription.Abstractions.Models.Input;
using Bakabase.Modules.Subscription.Abstractions.Services;
using Bakabase.Service.Components.Workflow;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// A subscription is a collection's external source. Checking it is not "diff a list of strings"
/// any more — it is "make this collection hold what the source is listing", which is the whole
/// reason a subscription is worth having: it fills a list you can then act on.
/// </summary>
[TestClass]
public sealed class SubscriptionAsCollectionSourceTests
{
    private IServiceProvider _sp = null!;

    /// <summary>A source whose listing the test controls.</summary>
    private sealed class FakeSource : ISubscriptionProvider
    {
        public static readonly List<SubscriptionItem> Items = [];

        public static SubscriptionSourceKind Kind_ = SubscriptionSourceKind.PlatformHolding;

        public static int FetchCount;

        public string Kind => "test.source";
        public string DisplayName => "Test source";
        public SubscriptionSourceKind SourceKind => Kind_;
        public ResourceSource? ResourceSource =>
            Kind_ == SubscriptionSourceKind.SharingChannel ? null : Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.DLsite;

        public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct) =>
            Task.FromResult(SubscriptionValidationResult.Valid);

        public string DescribeTarget(string targetJson) => "everything";

        public Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
            CancellationToken ct)
        {
            FetchCount++;

            return Task.FromResult<IReadOnlyList<SubscriptionItem>>(Items.ToList());
        }
    }

    /// <summary>A step that does nothing, so a recipe can exist without any real work happening.</summary>
    private sealed class NoopStep : Bakabase.Modules.Acquisition.Abstractions.Components.IAcquisitionStep
    {
        public const string StepKind = "acquisition.test.noop";

        public string Kind => StepKind;
        public string DisplayName => "Noop (test)";
        public Type? ConfigType => null;

        public Task<Bakabase.Modules.Acquisition.Abstractions.Components.AcquisitionStepOutcome> ExecuteAsync(
            Bakabase.Modules.Acquisition.Abstractions.Components.AcquisitionStepContext ctx,
            Bakabase.Modules.Acquisition.Abstractions.Models.Domain.AcquisitionWorkItem item,
            CancellationToken ct) =>
            Task.FromResult<Bakabase.Modules.Acquisition.Abstractions.Components.AcquisitionStepOutcome>(
                new Bakabase.Modules.Acquisition.Abstractions.Components.AcquisitionStepOutcome.Continue(item));
    }

    [TestInitialize]
    public async Task Setup()
    {
        FakeSource.Items.Clear();
        FakeSource.FetchCount = 0;
        FakeSource.Kind_ = SubscriptionSourceKind.PlatformHolding;

        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddSingleton<ISubscriptionProvider, FakeSource>();
            Bakabase.Modules.Acquisition.Extensions.ServiceCollectionExtensions
                .AddAcquisitionStep<NoopStep>(services);
        });
    }

    private ISubscriptionService Subscriptions => _sp.GetRequiredService<ISubscriptionService>();
    private ICollectionService Collections => _sp.GetRequiredService<ICollectionService>();

    private async Task<SubscriptionRecord> NewSubscription(string name = "A circle") =>
        await Subscriptions.CreateAsync(new SubscriptionCreationInputModel
        {
            Kind = "test.source",
            DisplayName = name,
            TargetJson = "{}",
        });

    private async Task<int[]> MemberIds(int collectionId) =>
        (await Collections.GetMembers(collectionId)).Select(m => m.ResourceId).Order().ToArray();

    private async Task<int> NotificationCount() =>
        (await _sp.GetRequiredService<INotificationService>().SearchAsync(new() {PageSize = 100})).Data?.Count ?? 0;

    /// <summary>A source needs somewhere to put what it finds, so making one makes that too.</summary>
    [TestMethod]
    public async Task ASubscriptionFillsACollectionOfItsOwnName()
    {
        var subscription = await NewSubscription("Circle X");

        Assert.IsNotNull(subscription.CollectionId);

        var collection = await Collections.Get(subscription.CollectionId!.Value);

        Assert.AreEqual("Circle X", collection!.Name);
    }

    /// <summary>
    /// The first check is the collection's seed. It is recorded like any other — announcing two
    /// hundred existing works as "new" would be true and useless.
    /// </summary>
    [TestMethod]
    public async Task TheFirstCheckFillsTheCollectionWithoutSayingAnything()
    {
        var subscription = await NewSubscription();

        FakeSource.Items.Add(new SubscriptionItem("RJ001", "Volume 1"));
        FakeSource.Items.Add(new SubscriptionItem("RJ002", "Volume 2"));

        var summary = await Subscriptions.RunCheckAsync(subscription.Id);

        Assert.IsTrue(summary!.FirstRun);
        Assert.AreEqual(2, (await MemberIds(subscription.CollectionId!.Value)).Length,
            "recorded — it is the collection's seed");
        Assert.AreEqual(0, await NotificationCount(), "but not announced");
    }

    [TestMethod]
    public async Task TheSecondCheckAnnouncesWhatIsActuallyNew()
    {
        var subscription = await NewSubscription();

        FakeSource.Items.Add(new SubscriptionItem("RJ001", "Volume 1"));
        await Subscriptions.RunCheckAsync(subscription.Id);

        FakeSource.Items.Add(new SubscriptionItem("RJ002", "Volume 2"));

        var summary = await Subscriptions.RunCheckAsync(subscription.Id);

        Assert.IsFalse(summary!.FirstRun);
        Assert.AreEqual(1, summary.NewItemCount, "one of the two is new; the other was already here");
        Assert.AreEqual(2, (await MemberIds(subscription.CollectionId!.Value)).Length);
        Assert.AreEqual(1, await NotificationCount());
    }

    /// <summary>
    /// Checking again with nothing new must be quiet. A source re-listing the same two hundred
    /// works every hour would otherwise be two hundred notifications an hour.
    /// </summary>
    [TestMethod]
    public async Task CheckingAgainWithNothingNewSaysNothing()
    {
        var subscription = await NewSubscription();

        FakeSource.Items.Add(new SubscriptionItem("RJ001", "Volume 1"));
        await Subscriptions.RunCheckAsync(subscription.Id);

        var second = await Subscriptions.RunCheckAsync(subscription.Id);

        Assert.AreEqual(0, second!.NewItemCount);
        Assert.AreEqual(0, await NotificationCount());
    }

    /// <summary>
    /// A shared item is one act of sharing, not one work. Two posts about the same thing are one
    /// resource — otherwise a collection fills up with duplicates of what you already have.
    /// </summary>
    [TestMethod]
    public async Task ASharedItemMatchesAResourceThatAlreadyHasThatName()
    {
        FakeSource.Kind_ = SubscriptionSourceKind.SharingChannel;

        var existing = await _sp.GetRequiredService<IPlaceholderResourceService>()
            .CreateByTitle("A Doujin Game");
        var subscription = await NewSubscription();

        FakeSource.Items.Add(new SubscriptionItem("tid-1", "A Doujin Game",
            "https://forum.example/read.php?tid=1"));

        await Subscriptions.RunCheckAsync(subscription.Id);

        CollectionAssert.AreEqual(new[] {existing.ResourceId},
            await MemberIds(subscription.CollectionId!.Value),
            "the post is about a thing already known, so it is that thing");
    }

    /// <summary>
    /// A member that stops being listed is marked, never removed. A circle taking a work off its
    /// page does not mean you no longer have it, and deleting the row would take the user's own
    /// notes and ignore-flags with it.
    /// </summary>
    [TestMethod]
    public async Task AMemberThatDisappearsFromTheSourceIsMarkedNotDeleted()
    {
        var subscription = await NewSubscription();

        FakeSource.Items.Add(new SubscriptionItem("RJ001", "Volume 1"));
        FakeSource.Items.Add(new SubscriptionItem("RJ002", "Volume 2"));
        await Subscriptions.RunCheckAsync(subscription.Id);

        var before = await Collections.GetMembers(subscription.CollectionId!.Value);
        var vanishing = before.Select(m => m.ResourceId).Order().Last();

        FakeSource.Items.RemoveAt(1);
        await Subscriptions.RunCheckAsync(subscription.Id);

        var after = await Collections.GetMembers(subscription.CollectionId!.Value);

        Assert.AreEqual(2, after.Count, "it is still a member");

        var stale = after.Single(m => m.ResourceId == vanishing);
        var stillListed = after.Single(m => m.ResourceId != vanishing);

        Assert.IsTrue(stale.LastSeenAt < stillListed.LastSeenAt,
            "but it was not seen this time, which is what the collection page shows");
    }

    /// <summary>
    /// Every source emits the same thing now, because by the time the event fires each item has
    /// become a resource. The per-provider item-type table this replaced had three entries and
    /// three places to update.
    /// </summary>
    [TestMethod]
    public void EverySourceEmitsAResource()
    {
        var trigger = _sp.GetServices<Bakabase.Modules.Workflow.Abstractions.Components.IWorkflowTrigger>()
            .Single(t => t.Kind == "subscription.updated");

        Assert.AreEqual(WorkflowItemTypes.Resource, trigger.ResolveOutputItemType(null));
        Assert.AreEqual(WorkflowItemTypes.Resource,
            trigger.ResolveOutputItemType("""{"kinds":["exhentai.search"]}"""));
    }

    /// <summary>
    /// A collection set to get things automatically does. A sharing channel's item carries its own
    /// link, so there is nothing else to wait for — which is the whole shape of the feature: a
    /// board is watched, something is shared, and it starts coming down without anyone looking.
    /// </summary>
    [TestMethod]
    public async Task ACollectionThatGetsThingsAutomaticallyStartsOnWhatArrives()
    {
        FakeSource.Kind_ = SubscriptionSourceKind.SharingChannel;

        var subscription = await NewSubscription();
        var collection = (await Collections.Get(subscription.CollectionId!.Value))!;

        await Collections.Put(collection.Id, new Bakabase.Modules.Collection.Models.Input.CollectionInputModel
        {
            Name = collection.Name,
            AutoAcquire = true,
        });

        // Something has to run when an acquisition starts; which recipe is not the point here.
        await _sp.GetRequiredService<Bakabase.Modules.Workflow.Abstractions.Services.IWorkflowDefinitionService>()
            .CreateAsync(new Bakabase.Modules.Workflow.Abstractions.Models.Input.WorkflowDefinitionCreationInputModel
            {
                Name = Bakabase.Modules.Acquisition.Components.BuiltinAcquisitionRecipes.DefaultRecipeNameFor(
                    Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants.AcquisitionLeadKind.SharedPage),
                TriggerKind = Bakabase.Modules.Acquisition.Components.Workflow.AcquisitionWorkflowKinds.TriggerRequested,
                Enabled = true,
                Activities = [new Bakabase.Modules.Workflow.Abstractions.Models.Input.WorkflowActivityInputModel
                {
                    Kind = NoopStep.StepKind, ConfigJson = "{}"
                }],
            });

        // The first check is the seed and announces nothing; it should not start acquiring the
        // whole board either.
        await Subscriptions.RunCheckAsync(subscription.Id);

        FakeSource.Items.Add(new SubscriptionItem("tid-1", "A Doujin Game",
            "https://forum.example/read.php?tid=1"));

        await Subscriptions.RunCheckAsync(subscription.Id);

        var tasks = await _sp.GetRequiredService<Bakabase.Modules.Acquisition.Abstractions.Services.IAcquisitionService>()
            .SearchAsync();

        Assert.AreEqual(1, tasks.Count, "the one thing that arrived, and only it");
        Assert.AreEqual(collection.Id, tasks[0].CollectionId);
    }

    /// <summary>Deleting the source leaves what it found. That is yours either way.</summary>
    [TestMethod]
    public async Task DeletingASubscriptionKeepsItsCollection()
    {
        var subscription = await NewSubscription();

        FakeSource.Items.Add(new SubscriptionItem("RJ001", "Volume 1"));
        await Subscriptions.RunCheckAsync(subscription.Id);

        await Subscriptions.DeleteAsync(subscription.Id);

        Assert.IsNotNull(await Collections.Get(subscription.CollectionId!.Value));
        Assert.AreEqual(1, (await MemberIds(subscription.CollectionId!.Value)).Length);
    }
}
