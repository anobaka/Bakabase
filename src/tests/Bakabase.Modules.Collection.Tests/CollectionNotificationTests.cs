using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Collection.Abstractions.Models.Db;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Models.Input;
using Bakabase.Modules.Collection.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Collection.Tests;

/// <summary>
/// When a collection changes, something has to say so.
/// <para>
/// A collection's numbers are the reason to have one, and they move for reasons that are not on
/// screen — a member is acquired, a rule catches something new. A page showing them finding out
/// on its next poll is how a collected ratio ends up lying for thirty seconds at a time.
/// </para>
/// </summary>
[TestClass]
public sealed class CollectionNotificationTests
{
    private IServiceProvider _sp = null!;

    private RecordingCollectionService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _service = new RecordingCollectionService(
            _sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, CollectionDbModel, int>>(),
            _sp.GetRequiredService<ICollectionResourceMappingService>(),
            _sp.GetRequiredService<IResourceService>(),
            _sp.GetRequiredService<IResourceDataChangeEventPublisher>());
    }

    private async Task<int> NewResource(string title) =>
        (await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(title)).ResourceId;

    [TestMethod]
    public async Task EveryChangeToACollectionIsAnnounced()
    {
        var id = (await _service.Add(new CollectionInputModel {Name = "A Series"})).Id;

        CollectionAssert.AreEqual(new[] {id}, _service.Changed.ToArray(), "creating one");

        _service.Changed.Clear();

        var resourceId = await NewResource("Volume 1");

        await _service.AddMembers(id, [resourceId]);
        await _service.SetMemberIgnored(id, resourceId, true);
        await _service.ReorderMembers(id, [resourceId]);
        await _service.RemoveMembers(id, [resourceId]);
        await _service.Put(id, new CollectionInputModel {Name = "Renamed"});

        Assert.AreEqual(5, _service.Changed.Count,
            "adding, ignoring, reordering, removing and editing all move what a page shows");
        Assert.IsTrue(_service.Changed.All(x => x == id));
    }

    [TestMethod]
    public async Task AddingNobodyAnnouncesNothing()
    {
        var id = (await _service.Add(new CollectionInputModel {Name = "A Series"})).Id;
        var resourceId = await NewResource("Volume 1");

        await _service.AddMembers(id, [resourceId]);
        _service.Changed.Clear();

        // A subscription that re-lists the same thing every hour would otherwise announce a
        // change every hour, and every open page would reload for nothing.
        await _service.AddMembers(id, [resourceId]);

        Assert.AreEqual(0, _service.Changed.Count);
    }

    [TestMethod]
    public async Task DeletingACollectionSaysSoSeparately()
    {
        var id = (await _service.Add(new CollectionInputModel {Name = "A Series"})).Id;

        _service.Changed.Clear();
        await _service.Delete(id);

        Assert.AreEqual(0, _service.Changed.Count, "there is nothing left to describe");
        CollectionAssert.AreEqual(new[] {id}, _service.Removed.ToArray());
    }

    /// <summary>The module's hooks, recorded rather than pushed anywhere.</summary>
    private sealed class RecordingCollectionService(
        FullMemoryCacheResourceService<BakabaseDbContext, CollectionDbModel, int> orm,
        ICollectionResourceMappingService mappings,
        IResourceService resources,
        IResourceDataChangeEventPublisher changePublisher)
        : CollectionService<BakabaseDbContext>(orm, mappings, resources, changePublisher)
    {
        public List<int> Changed { get; } = [];

        public List<int> Removed { get; } = [];

        protected override Task OnCollectionChanged(int collectionId, CancellationToken ct)
        {
            Changed.Add(collectionId);

            return Task.CompletedTask;
        }

        protected override Task OnCollectionRemoved(int collectionId, CancellationToken ct)
        {
            Removed.Add(collectionId);

            return Task.CompletedTask;
        }
    }
}
