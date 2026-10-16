using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bakabase.Service.Models.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class MetadataReplicaTests
{
    private string _directory = null!;
    private MetadataReplicaStore _store = null!;
    private readonly ReplicaIdentity _owner = new("owner-a", "epoch-one");

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bakabase-replica-tests-" + Guid.NewGuid().ToString("N"));
        _store = new MetadataReplicaStore(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    private static ReplicaResource Resource(int id, params ReplicaPosting[] postings) => new(id, 1,
        JsonConvert.SerializeObject(new {id, displayName = "resource-" + id}),
        new ReplicaResourceSort {Path = $"Z:\\library\\file-{id}.mkv", FileName = $"file-{id}.mkv", CreateDt = new DateTime(2026, 1, 1).AddSeconds(id)}, postings);

    private async Task<ReplicaCheckpoint> SeedAsync(ReplicaIdentity owner, IReadOnlyList<ReplicaResource> resources,
        IReadOnlyList<ReplicaCatalog>? catalog = null, string generation = "snapshot-one")
    {
        var checkpoint = await _store.BeginSnapshotAsync(owner, generation);
        foreach (var chunk in resources.Chunk(256))
        {
            var next = checkpoint with {Cursor = chunk[^1].ResourceId.ToString()};
            await _store.ApplyBatchAsync(owner, new(checkpoint, next, chunk, [], [], []));
            checkpoint = next;
        }
        var complete = checkpoint with {Complete = true};
        await _store.ApplyBatchAsync(owner, new(checkpoint, complete, [], [], catalog ?? [], []));
        return complete;
    }

    [TestMethod]
    public async Task Restart_resumes_snapshot_without_exposing_partial_rows_and_publishes_atomically()
    {
        var initial = await _store.BeginSnapshotAsync(_owner, "one");
        var middle = initial with {Cursor = "resource:1"};
        await _store.ApplyBatchAsync(_owner, new(initial, middle, [Resource(1)], [], [], []));
        _store = new MetadataReplicaStore(_directory);
        Assert.AreEqual(middle, (await _store.GetStatusAsync(_owner)).Checkpoint);
        Assert.AreEqual(middle, await _store.BeginSnapshotAsync(_owner, "one"));
        await Assert.ThrowsExactlyAsync<ReplicaNotReadyException>(() => _store.GetByIdsAsync(_owner, [1]));
        Assert.AreEqual(1, (await _store.GetStatusAsync(_owner)).StagedResourceCount);
        var complete = middle with {Complete = true};
        await _store.ApplyBatchAsync(_owner, new(middle, complete, [Resource(2)], [], [], []));
        Assert.AreEqual(2, (await _store.GetByIdsAsync(_owner, [1, 2])).Count);
        var rebuilding = await _store.BeginSnapshotAsync(_owner, "two");
        var rebuildingMiddle = rebuilding with {Cursor = "resource:3"};
        await _store.ApplyBatchAsync(_owner, new(rebuilding, rebuildingMiddle, [Resource(3)], [], [], []));
        Assert.AreEqual(2, (await _store.GetByIdsAsync(_owner, [1, 2, 3])).Count);
        await _store.ApplyBatchAsync(_owner, new(rebuildingMiddle, rebuildingMiddle with {Complete = true}, [], [], [], []));
        CollectionAssert.AreEqual(new[] {3}, (await _store.GetRowsByIdsAsync(_owner, [1, 2, 3])).Select(r => r.ResourceId).ToArray());
    }

    [TestMethod]
    public async Task Page_retry_and_stale_checkpoint_cannot_duplicate_or_overwrite_rows()
    {
        var complete = await SeedAsync(_owner, [Resource(1), Resource(2)]);
        var next = complete with {Sequence = 5};
        var batch = new ReplicaBatch(complete, next, [Resource(3) with {Revision = 5}], [1], [], []);
        await _store.ApplyBatchAsync(_owner, batch);
        await _store.ApplyBatchAsync(_owner, batch);
        await Assert.ThrowsExactlyAsync<ReplicaCheckpointConflictException>(() =>
            _store.ApplyBatchAsync(_owner, new(complete, complete with {Sequence = 6}, [Resource(4)], [2], [], [])));
        var rows = await _store.GetRowsByIdsAsync(_owner, [1, 2, 3, 4]);
        CollectionAssert.AreEqual(new[] {2, 3}, rows.Select(r => r.ResourceId).ToArray());
        Assert.AreEqual(5L, rows[1].Revision);
        Assert.AreEqual(next, (await _store.GetStatusAsync(_owner)).Checkpoint);
    }

    [TestMethod]
    public async Task Failure_halfway_through_batch_rolls_back_deletion_resource_and_cursor()
    {
        var complete = await SeedAsync(_owner, [Resource(1), Resource(2) with {Revision = 20}]);
        await Assert.ThrowsExactlyAsync<ReplicaCheckpointConflictException>(() => _store.ApplyBatchAsync(_owner,
            new(complete, complete with {Sequence = 1}, [Resource(3), Resource(2)], [1], [], [])));
        CollectionAssert.AreEqual(new[] {1, 2}, (await _store.GetRowsByIdsAsync(_owner, [1, 2, 3])).Select(r => r.ResourceId).ToArray());
        Assert.AreEqual(complete, (await _store.GetStatusAsync(_owner)).Checkpoint);
    }

    [TestMethod]
    public async Task Same_numeric_resource_ids_are_isolated_by_owner_and_epoch_and_controls_survive_restart()
    {
        await SeedAsync(_owner, [Resource(1)]);
        var other = new ReplicaIdentity("owner-b", "epoch-one");
        await SeedAsync(other, [Resource(1) with {ResourceJson = "{\"id\":1,\"displayName\":\"other\"}"}]);
        StringAssert.Contains((await _store.GetByIdsAsync(other, [1]))[0], "other");
        StringAssert.Contains((await _store.GetByIdsAsync(_owner, [1]))[0], "resource-1");
        await Assert.ThrowsExactlyAsync<ReplicaNotReadyException>(() => _store.GetByIdsAsync(_owner with {LibraryEpoch = "epoch-two"}, [1]));
        var controls = new ReplicaSyncState(true, DateTimeOffset.UtcNow, "Offline", "Owner is offline", true);
        await _store.SetSyncStateAsync(_owner, controls);
        Assert.AreEqual(controls, await new MetadataReplicaStore(_directory).GetSyncStateAsync(_owner));
        Assert.IsFalse((await _store.GetSyncStateAsync(other)).Paused);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task Application_json_defaults_cannot_change_replica_identity_path_or_checkpoint()
    {
        var saved = JsonConvert.DefaultSettings;
        try
        {
            JsonConvert.DefaultSettings = null;
            var checkpoint = await SeedAsync(_owner, [Resource(1)]);
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(), NullValueHandling = NullValueHandling.Ignore
            };
            Assert.AreEqual(checkpoint, (await _store.GetStatusAsync(_owner)).Checkpoint);
            await _store.ApplyBatchAsync(_owner, new(checkpoint, checkpoint with {Sequence = 1}, [Resource(2)], [], [], []));
            Assert.AreEqual(2, (await _store.GetByIdsAsync(_owner, [1, 2])).Count);
            Assert.AreEqual(1, Directory.GetFiles(_directory, "*.sqlite").Length);
        }
        finally { JsonConvert.DefaultSettings = saved; }
    }

    [TestMethod]
    public async Task Catalog_updates_deletions_and_cursor_commit_with_resources()
    {
        var cp = await SeedAsync(_owner, [Resource(1)], [new("properties", "4:1", "{\"name\":\"first\"}")]);
        await _store.ApplyBatchAsync(_owner, new(cp, cp with {Sequence = 2}, [Resource(2)], [1],
            [new("properties", "4:2", "{\"name\":\"second\"}")], [new("properties", "4:1")]));
        var catalog = await _store.ReadCatalogAsync(_owner, "properties", limit: 1);
        Assert.AreEqual("4:2", catalog.Single().Key);
        Assert.AreEqual(0, (await _store.ReadCatalogAsync(_owner, "properties", afterKey: "4:2")).Count);
    }

    [TestMethod]
    public async Task All_property_descriptor_operations_match_whole_index_across_resource_chunks()
    {
        var properties = Enum.GetValues<PropertyType>().Select(type => new Property(PropertyPool.Custom, (int)type, type)).ToArray();
        var resources = Enumerable.Range(1, 600).Select(id =>
        {
            var postings = properties.SelectMany(p =>
            {
                var provider = (IPropertyIndexProvider)PropertySystem.Property.GetDescriptor(p.Type);
                // Missing values and two conflicting scopes exercise complement semantics.
                var values = id % 5 == 0 ? Array.Empty<object>() : id % 3 == 0
                    ? new[] {Sample(PropertySystem.Property.GetDbValueType(p.Type), 0), Sample(PropertySystem.Property.GetDbValueType(p.Type), 1)}
                    : [Sample(PropertySystem.Property.GetDbValueType(p.Type), id % 2)];
                return values.SelectMany(value => provider.GenerateIndexEntries(p, value)).Select(entry =>
                    new ReplicaPosting(p.Pool, p.Id, entry.Key.Trim().ToLowerInvariant(), entry.RangeValue == null ? null : ReplicaRangeValue.From(entry.RangeValue)));
            }).ToArray();
            return Resource(id, postings);
        }).ToArray();
        await SeedAsync(_owner, resources);
        var query = new MetadataReplicaQuery(_store);
        var allIds = resources.Select(r => r.ResourceId).ToHashSet();
        var checkedOperations = 0;
        foreach (var property in properties)
        {
            var handler = PropertySystem.Property.GetSearchHandler(property.Type);
            var values = new Dictionary<string, HashSet<int>>();
            var ranges = new SortedList<IComparable, HashSet<int>>();
            foreach (var resource in resources)
                foreach (var posting in resource.Postings.Where(p => p.PropertyId == property.Id))
                {
                    if (posting.ValueKey != null)
                    {
                        if (!values.TryGetValue(posting.ValueKey, out var ids)) values[posting.ValueKey] = ids = [];
                        ids.Add(resource.ResourceId);
                    }
                    if (posting.RangeValue != null)
                    {
                        var range = posting.RangeValue.ToComparable();
                        if (!ranges.TryGetValue(range, out var ids)) ranges[range] = ids = [];
                        ids.Add(resource.ResourceId);
                    }
                }
            foreach (var (operation, options) in handler.SearchOperations)
            {
                var filter = new ResourceSearchFilter
                {
                    Property = property, PropertyPool = property.Pool, PropertyId = property.Id, Operation = operation,
                    DbValue = options == null ? null : Sample(PropertySystem.Property.GetDbValueType(options.AsType), 0)
                };
                var expected = PropertySystem.Search.EvaluateOnIndex(filter, values,
                    operation == SearchOperation.NotEquals && ranges.Count == 0 ? null : ranges.ToList(), allIds);
                Assert.IsNotNull(expected, $"Baseline descriptor {property.Type}/{operation} must support its advertised operation.");
                var result = await query.SearchAsync(_owner, new ResourceSearch
                {
                    PageIndex = 1, PageSize = 100, Group = new() {Combinator = SearchCombinator.And, Filters = [filter]}
                });
                Assert.AreEqual((long)expected.Count, result.TotalCount, $"{property.Type}/{operation}");
                CollectionAssert.AreEqual(expected.OrderDescending().Take(100).ToArray(), Ids(result), $"{property.Type}/{operation}");
                checkedOperations++;
            }
        }
        Assert.IsTrue(checkedOperations > 100);
    }

    [TestMethod]
    public async Task Input_uses_owner_catalog_original_singlechoice_type_for_in_and_keyword_options()
    {
        var property = new Property(PropertyPool.Custom, 1, PropertyType.SingleChoice, "Owner choice",
            new SingleChoicePropertyOptions {Choices = [new ChoiceOptions {Value = "x", Label = "Owner Label"}, new ChoiceOptions {Value = "y", Label = "Other"}]});
        var cp = await SeedAsync(_owner,
            [Resource(1, new ReplicaPosting(PropertyPool.Custom, 1, "x")), Resource(2, new ReplicaPosting(PropertyPool.Custom, 1, "y")), Resource(3)],
            [new("properties", "4:1", JsonConvert.SerializeObject(property))]);
        var query = new MetadataReplicaQuery(_store);
        var input = new ResourceSearchInputModel
        {
            Page = 1, PageSize = 100, Group = new() {Combinator = SearchCombinator.And, Filters =
                [new() {PropertyPool = PropertyPool.Custom, PropertyId = 1, Operation = SearchOperation.In, DbValue = new List<string> {"x"}.SerializeAsStandardValue(StandardValueType.ListString)}]}
        };
        CollectionAssert.AreEqual(new[] {1}, Ids(await query.SearchAsync(_owner, input)));
        input.Group.Filters![0].Operation = SearchOperation.NotIn;
        CollectionAssert.AreEqual(new[] {3, 2}, Ids(await query.SearchAsync(_owner, input)));
        CollectionAssert.AreEqual(new[] {1}, Ids(await query.SearchAsync(_owner, new ResourceSearchInputModel {Page = 1, PageSize = 100, Keyword = "Owner Label"})));
        await _store.ApplyBatchAsync(_owner, new(cp, cp with {Sequence = 1}, [], [], [], [new("properties", "4:1")]));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => query.SearchAsync(_owner, input));
    }

    [TestMethod]
    public async Task Rule_scan_preserves_db_filters_keywords_and_bounded_generation_pinned_continuation()
    {
        var choice = new Property(PropertyPool.Custom, 1, PropertyType.SingleChoice, "Choice",
            new SingleChoicePropertyOptions {Choices = [new ChoiceOptions {Value = "x", Label = "Owner Label"}, new ChoiceOptions {Value = "y", Label = "Other"}]});
        var rows = Enumerable.Range(1, 600).Select(id => Resource(id,
            new ReplicaPosting(PropertyPool.Custom, 1, id % 2 == 0 ? "x" : "y"))).ToArray();
        var checkpoint = await SeedAsync(_owner, rows,
            [new("properties", "4:1", JsonConvert.SerializeObject(choice))]);
        var search = JsonConvert.SerializeObject(new Bakabase.Modules.Search.Models.Db.ResourceSearchDbModel
        {
            Keyword = "Owner Label",
            Group = new() {Combinator = SearchCombinator.And, Filters =
                [new() {PropertyPool = PropertyPool.Custom, PropertyId = 1, Operation = SearchOperation.In,
                    Value = new List<string> {"x"}.SerializeAsStandardValue(StandardValueType.ListString)}]}
        });
        var query = new MetadataReplicaQuery(_store);
        var first = await query.ScanRuleIdsAsync(_owner, search, null, 0, null, default);
        Assert.AreEqual(256, first.AfterId);
        Assert.AreEqual(600, first.UpperId);
        CollectionAssert.AreEqual(Enumerable.Range(1, 128).Select(i => i * 2).ToArray(), first.Ids);
        Assert.IsFalse(first.Complete);
        // New IDs arriving during a job are left to the next job, not appended to this scan.
        await _store.ApplyBatchAsync(_owner, new(checkpoint, checkpoint with {Sequence = 1},
            [Resource(601, new ReplicaPosting(PropertyPool.Custom, 1, "x"))], [], [], []));
        var second = await query.ScanRuleIdsAsync(_owner, search, first.Generation, first.AfterId, first.UpperId, default);
        var third = await query.ScanRuleIdsAsync(_owner, search, first.Generation, second.AfterId, first.UpperId, default);
        Assert.IsTrue(third.Complete);
        Assert.AreEqual(600, third.AfterId);
        CollectionAssert.AreEqual(Enumerable.Range(1, 300).Select(i => i * 2).ToArray(), first.Ids.Concat(second.Ids).Concat(third.Ids).ToArray());
        var impossible = search.Replace("Owner Label", "No matching label");
        Assert.IsEmpty((await query.ScanRuleIdsAsync(_owner, impossible, first.Generation, 0, first.UpperId, default)).Ids);
        await SeedAsync(_owner, [], generation: "next-generation");
        await Assert.ThrowsExactlyAsync<ReplicaCheckpointConflictException>(() =>
            query.ScanRuleIdsAsync(_owner, search, first.Generation, third.AfterId, first.UpperId, default));
    }

    [TestMethod]
    public async Task Group_null_disabled_empty_and_tags_match_native_algebra()
    {
        var p = new Property(PropertyPool.Custom, 1, PropertyType.SingleLineText);
        await SeedAsync(_owner, [Resource(1, new ReplicaPosting(PropertyPool.Custom, 1, "yes")), Resource(2) with {Sort = new() {Tags = ResourceTag.Pinned}}, Resource(3)]);
        var query = new MetadataReplicaQuery(_store);
        var filter = new ResourceSearchFilter {Property = p, PropertyPool = p.Pool, PropertyId = p.Id, Operation = SearchOperation.Equals, DbValue = "yes"};
        var domain = new ResourceSearch {PageIndex = 1, PageSize = 10, Group = new() {Combinator = SearchCombinator.Or, Filters = [filter], Groups = [new() {Combinator = SearchCombinator.And}]}};
        Assert.AreEqual(3L, (await query.SearchAsync(_owner, domain)).TotalCount);
        domain.Group.Combinator = SearchCombinator.And;
        CollectionAssert.AreEqual(new[] {1}, Ids(await query.SearchAsync(_owner, domain)));
        domain.Group.Filters![0].DbValue = " ";
        Assert.AreEqual(3L, (await query.SearchAsync(_owner, domain)).TotalCount);
        domain.Tags = [ResourceTag.Pinned];
        CollectionAssert.AreEqual(new[] {2}, Ids(await query.SearchAsync(_owner, domain)));
        domain.Group.Combinator = (SearchCombinator)999;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => query.SearchAsync(_owner, domain));
    }

    [TestMethod]
    public async Task Reference_counts_use_local_filter_results_and_count_each_resource_once_across_scopes()
    {
        var choice = new Property(PropertyPool.Custom, 1, PropertyType.SingleChoice, "Choice", new SingleChoicePropertyOptions
        {
            Choices = [new ChoiceOptions {Value = "alpha", Label = "Alpha"}, new ChoiceOptions {Value = "beta", Label = "Beta"}, new ChoiceOptions {Value = "unused", Label = "Unused"}]
        });
        var text = new Property(PropertyPool.Custom, 2, PropertyType.SingleLineText);
        await SeedAsync(_owner,
        [Resource(1, new ReplicaPosting(PropertyPool.Custom, 1, "alpha"), new(PropertyPool.Custom, 1, "alpha"), new(PropertyPool.Custom, 2, "yes")),
            Resource(2, new ReplicaPosting(PropertyPool.Custom, 1, "beta"), new(PropertyPool.Custom, 2, "no")),
            Resource(3, new ReplicaPosting(PropertyPool.Custom, 1, "alpha"), new(PropertyPool.Custom, 2, "no"))],
        [new("properties", "4:1", JsonConvert.SerializeObject(choice)), new("properties", "4:2", JsonConvert.SerializeObject(text))]);
        var query = new MetadataReplicaQuery(_store);
        var counts = await query.PropertyValueResourceCountsAsync(_owner, PropertyPool.Custom, 1, new());
        Assert.AreEqual(2, counts["alpha"]);
        Assert.AreEqual(1, counts["beta"]);
        Assert.AreEqual(0, counts["unused"]);
        counts = await query.PropertyValueResourceCountsAsync(_owner, PropertyPool.Custom, 1, new()
        {
            Group = new() {Combinator = SearchCombinator.And, Filters = [new() {PropertyPool = PropertyPool.Custom, PropertyId = 2, Operation = SearchOperation.Equals, DbValue = "yes"}]}
        });
        Assert.AreEqual(1, counts["alpha"]);
        Assert.AreEqual(0, counts["beta"]);
    }

    [TestMethod]
    public async Task Offline_keyword_recommendations_keep_owner_labels_and_original_text_casing()
    {
        var text = new Property(PropertyPool.Custom, 1, PropertyType.SingleLineText);
        var choice = new Property(PropertyPool.Custom, 2, PropertyType.SingleChoice, "Choice", new SingleChoicePropertyOptions
        {
            Choices = [new ChoiceOptions {Value = "choice-id", Label = "Alpha Choice"}]
        });
        var resource = Resource(1) with {ResourceJson = JsonConvert.SerializeObject(new
        {
            id = 1, properties = new Dictionary<int, object>
            {
                [4] = new Dictionary<int, object> {[1] = new {values = new[] {new {value = "Alpha Text"}}}, [2] = new {values = new[] {new {value = "choice-id"}}}}
            }
        })};
        await SeedAsync(_owner, [resource], [new("properties", "4:1", JsonConvert.SerializeObject(text)),
            new("properties", "4:2", JsonConvert.SerializeObject(choice)), new("mediaLibraries", "1", "{\"name\":\"Alpha Library\"}")]);
        var result = await new MetadataReplicaQuery(_store).KeywordRecommendationsAsync(_owner, "Alpha");
        CollectionAssert.AreEquivalent(new[] {"Alpha Library", "Alpha Text", "Alpha Choice"}, result.ToArray());
        Assert.AreEqual(1, (await new MetadataReplicaQuery(_store).KeywordRecommendationsAsync(_owner, "Alpha", maxCount: 1)).Count);
    }

    [TestMethod]
    public async Task Source_filename_and_missing_values_sort_correctly_on_another_operating_system()
    {
        await SeedAsync(_owner,
        [Resource(1) with {Sort = new() {Path = "Z:\\a.mkv", FileName = "a.mkv", HealthScore = 2m}},
            Resource(2) with {Sort = new() {Path = "Z:\\B.mkv", FileName = "B.mkv", HealthScore = 10m}},
            Resource(3) with {Sort = new()}]);
        var query = new MetadataReplicaQuery(_store);
        foreach (var order in new[] {ResourceSearchSortableProperty.Filename, ResourceSearchSortableProperty.HealthScore})
        {
            var input = new ResourceSearch {PageIndex = 1, PageSize = 100, Orders = [new() {Property = order, Asc = true}]};
            CollectionAssert.AreEqual(new[] {1, 2, 3}, Ids(await query.SearchAsync(_owner, input)));
            input.Orders[0].Asc = false;
            CollectionAssert.AreEqual(new[] {2, 1, 3}, Ids(await query.SearchAsync(_owner, input)));
        }
    }

    [TestMethod]
    public async Task Oversize_batch_is_rejected_without_checkpoint_change()
    {
        var cp = await _store.BeginSnapshotAsync(_owner, "one");
        await Assert.ThrowsExactlyAsync<ReplicaBudgetExceededException>(() => _store.ApplyBatchAsync(_owner,
            new(cp, cp with {Complete = true}, Enumerable.Range(1, 513).Select(id => Resource(id)).ToArray(), [], [], [])));
        Assert.AreEqual(cp, (await _store.GetStatusAsync(_owner)).Checkpoint);
        await Assert.ThrowsExactlyAsync<ReplicaBudgetExceededException>(() => _store.ApplyBatchAsync(_owner,
            new(cp, cp with {Complete = true}, [Resource(1) with {ResourceJson = new string('x', MetadataReplicaStore.MaxResourceBytes + 1)}], [], [], [])));
        Assert.AreEqual(0L, (await _store.GetStatusAsync(_owner)).StagedResourceCount);
    }

    [TestMethod]
    [TestCategory("Scale")]
    public async Task One_hundred_thousand_resources_and_one_million_postings_are_streamed_and_searchable_offline()
    {
        var cp = await _store.BeginSnapshotAsync(_owner, "scale");
        for (var start = 1; start <= 100_000; start += 500)
        {
            var rows = Enumerable.Range(start, 500).Select(id => Resource(id,
                Enumerable.Range(1, 10).Select(p => new ReplicaPosting(PropertyPool.Custom, p, id % 2 == 0 ? "even" : "odd")).ToArray())).ToArray();
            var next = cp with {Cursor = rows[^1].ResourceId.ToString()};
            await _store.ApplyBatchAsync(_owner, new(cp, next, rows, [], [], []));
            cp = next;
        }
        await _store.ApplyBatchAsync(_owner, new(cp, cp with {Complete = true}, [], [], [], []));
        _store = new MetadataReplicaStore(_directory);
        var result = await new MetadataReplicaQuery(_store).SearchAsync(_owner, new ResourceSearch
        {
            PageIndex = 500, PageSize = 100, Group = new() {Combinator = SearchCombinator.And, Filters =
            [new() {PropertyPool = PropertyPool.Custom, PropertyId = 1, Property = new(PropertyPool.Custom, 1, PropertyType.SingleLineText), Operation = SearchOperation.Equals, DbValue = "even"}]}
        });
        Assert.AreEqual(50_000L, result.TotalCount);
        CollectionAssert.AreEqual(Enumerable.Range(1, 100).Reverse().Select(i => i * 2).ToArray(), Ids(result));
    }

    private static int[] Ids(ReplicaSearchResult result) => result.ResourceJson.Select(json => JObject.Parse(json)["id"]!.Value<int>()).ToArray();
    private static object Sample(StandardValueType type, int variant) => type switch
    {
        StandardValueType.String => variant == 0 ? "alpha" : "beta",
        StandardValueType.ListString => new List<string> {variant == 0 ? "alpha" : "beta"},
        StandardValueType.Decimal => variant == 0 ? 1m : 2m,
        StandardValueType.Boolean => variant == 0,
        StandardValueType.DateTime => new DateTime(2026, 1, 1).AddDays(variant),
        StandardValueType.Time => TimeSpan.FromHours(variant + 1),
        StandardValueType.Link => new LinkValue(variant == 0 ? "alpha" : "beta", "https://example.test/" + variant),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
