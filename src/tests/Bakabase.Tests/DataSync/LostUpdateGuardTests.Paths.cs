using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Components.Enhancers.Regex;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Services;
using Bootstrap.Components.Tasks;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using ApplyFixture = Bakabase.Tests.DataSync.Apply.DataSyncApplyFixture;
using ApplyPeer = Bakabase.Tests.DataSync.Apply.DataSyncPeer;
using DataSyncChildChange = Bakabase.InsideWorld.Business.Components.DataSync.Apply.DataSyncChildChange;
using DataSyncEntityChanges = Bakabase.InsideWorld.Business.Components.DataSync.Apply.DataSyncEntityChanges;
using static Bakabase.Tests.DataSync.DataSyncRefreshFixture;

namespace Bakabase.Tests.DataSync;

// §13.5 LostUpdateGuardTests, the writers and the waiting records: the controller-driven enhancer (F74) as a whole-row
// writer, and a newer peer revision that arrives while the entity is held.
public partial class LostUpdateGuardTests
{
    private const string CaptureRegex = @"\[(?<studio>[^\]]+)\]";

    /// <summary>
    /// F74: <c>EnhancementController</c>'s enhance-with-options action calls <c>EnhancerService</c> directly, outside
    /// any BTask, and it writes the custom properties it read at its start back whole with <c>UpdateRange</c>. A sync
    /// apply that committed between that read and that write is undone; Refresh must hold the entity, not publish the
    /// stale row as a newer revision.
    /// </summary>
    [TestMethod]
    public async Task A_stale_write_through_the_controller_driven_enhancer_is_held()
    {
        var hook = new CustomPropertyWriteHook();
        var f = await DataSyncRefreshFixture.CreateAsync(kind: DataSyncKindIds.ExtensionGroup, configure: s =>
        {
            s.AddSingleton(hook);
            s.AddScoped<ICustomPropertyService>(sp => InterceptedCustomProperties.Wrap(
                ActivatorUtilities.CreateInstance<CustomPropertyService<BakabaseDbContext>>(sp), hook));
            s.AddScoped<IDataSyncKind>(sp => new CustomPropertyChoicesKind(sp.GetRequiredService<ICustomPropertyService>()));
        });
        var root = Path.Combine(Path.GetTempPath(), $"LostUpdateGuardTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "[Toei] My Show"));
        try
        {
            var services = f.Services;
            var properties = services.GetRequiredService<ICustomPropertyService>();
            var studio = await properties.Add(new CustomPropertyAddOrPutDto
            {
                Name = "Studio",
                Type = PropertyType.MultipleChoice,
                Options = JsonConvert.SerializeObject(new MultipleChoicePropertyOptions
                {
                    Choices = [new ChoiceOptions {Label = "Sunrise", Value = "s"}],
                }),
            });
            var localKey = studio.Id.ToString();
            var resourceId = await EnhanceableResourceAsync(services, root, studio.Id);
            await RefreshPropertiesAsync(f);

            // Between the enhancer's read and its whole-row write, a sync apply adds "Mystery" and records it.
            DataSyncEntityDbModel? applied = null;
            hook.BeforeUpdateRange = async _ =>
            {
                hook.BeforeUpdateRange = null;
                await using (var scope = services.CreateAsyncScope())
                {
                    var service = scope.ServiceProvider.GetRequiredService<ICustomPropertyService>();
                    var current = await service.GetByKey(studio.Id);
                    var options = (MultipleChoicePropertyOptions) current.Options!;
                    options.Choices!.Add(new ChoiceOptions {Label = "Mystery", Value = "c"});
                    await service.UpdateRange([current.ToDbModel()!]);
                }

                await RefreshPropertiesAsync(f);
                await f.LogApplyAsync(DataSyncHistoryKind.AutoSync, new DataSyncEntityChanges(DataSyncKindIds.CustomProperty,
                    localKey, [], [new DataSyncChildChange("choice:pc", null, Child("c", "Mystery"))]));
                applied = await f.Db.DataSyncEntities.AsNoTracking()
                    .SingleAsync(e => e.Kind == DataSyncKindIds.CustomProperty && e.LocalKey == localKey);
            };

            // As the controller calls it: in the request's own scope, outside any BTask.
            await using (var request = services.CreateAsyncScope())
            {
                await request.ServiceProvider.GetRequiredService<IEnhancerService>().EnhanceResourceWithOptions(resourceId,
                    [new EnhancerFullOptions {EnhancerId = (int) EnhancerId.Regex, Expressions = [CaptureRegex]}],
                    CancellationToken.None);
            }

            Assert.IsNull(hook.BeforeUpdateRange, "the enhancer wrote the property back whole");
            var labels = ((MultipleChoicePropertyOptions) (await properties.GetByKey(studio.Id)).Options!).Choices!
                .Select(c => c.Label).ToArray();
            CollectionAssert.AreEquivalent(new[] {"Sunrise", "Toei"}, labels, "its stale copy dropped what sync added");
            var result = await RefreshPropertiesAsync(f);

            Assert.AreEqual(0, result.Changed, "the stale overwrite is not published");
            var held = await f.Db.DataSyncEntities.AsNoTracking()
                .SingleAsync(e => e.Kind == DataSyncKindIds.CustomProperty && e.LocalKey == localKey);
            Assert.IsTrue(held.PublishHeld);
            Assert.AreEqual(applied!.VvJson, held.VvJson, "no revision after what the apply recorded");
            var item = (await f.ItemsAsync()).Single();
            Assert.AreEqual(DataSyncInboxItemType.SuspectedLostUpdate, item.Type);
            Assert.AreEqual("choice:pc", DataSyncStoredJson.Read<DataSyncInboxPayload>(item.PayloadJson, "").Fields.Single().Path);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// A newer peer revision of a held entity waits (§8.4 row F): stored as a <c>PublishHeld</c> pending record,
    /// nothing applied, the base where it was. Once the person decides it is merged, whichever way they chose.
    /// </summary>
    [TestMethod]
    [DataRow(DataSyncInboxAction.Publish)]
    [DataRow(DataSyncInboxAction.Reapply)]
    public async Task A_newer_peer_revision_waits_while_held_and_is_merged_once_decided(DataSyncInboxAction action)
    {
        var f = await ApplyFixture.CreateAsync();
        var peer = new ApplyPeer("PC-1");
        var link = await f.LinkAsync(peer);
        var key = SyncKey.New().Value;
        var v1 = peer.Next();
        await f.ApplyAsync(link, peer, (ApplyFixture.Item,
            peer.Record([key], v1, ApplyFixture.Content("Genre", ("a", "Rock"), ("b", "Jazz")), "a0")));
        var localKey = f.Kind.KeyOf("Genre");
        var v2 = peer.Next(v1);
        await f.ApplyAsync(link, peer, (ApplyFixture.Item,
            peer.Record([key], v2, ApplyFixture.Content("Genre", ("a", "Rock"), ("b", "Jazz"), ("c", "Pop")), "a0")));
        // A whole-row writer that read before the apply writes back afterwards: "Pop" is gone again, and it renames.
        f.Kind.Definitions[localKey] = ApplyFixture.Content("Genre 2", ("a", "Rock"), ("b", "Jazz"));
        await f.RefreshAsync();
        var held = await f.RowAsync(localKey);
        Assert.IsTrue(held.PublishHeld);

        // Meanwhile the peer adds "Blues".
        var v3 = peer.Next(v2);
        var outcome = await f.ApplyAsync(link, peer, (ApplyFixture.Item,
            peer.Record([key], v3, ApplyFixture.Content("Genre", ("a", "Rock"), ("b", "Jazz"), ("c", "Pop"), ("d", "Blues")), "a0")));

        Assert.AreEqual(0, outcome.Applied);
        var waiting = (await f.BasesAsync(link.Id)).Single(b => b.SyncKey == held.SyncKey);
        Assert.AreEqual((DataSyncPendingReason?) DataSyncPendingReason.PublishHeld, waiting.PendingReason,
            "incoming merges of a held entity freeze (§8.4 row F)");
        Assert.AreEqual(v2.ToCanonicalString(), waiting.VvJson, "its base did not advance");
        var frozen = await f.RowAsync(localKey);
        Assert.AreEqual((held.VvJson, held.LocalHash, true), (frozen.VvJson, frozen.LocalHash, frozen.PublishHeld));
        CollectionAssert.AreEqual(new[] {"Rock", "Jazz"}, f.Kind[localKey].Children.Select(c => c.Label).ToArray(),
            "nothing applied");

        var item = (await f.OpenItemsAsync()).Single(i => i.Type == DataSyncInboxItemType.SuspectedLostUpdate);
        await f.ResolveAsync(item, action);

        var content = f.Kind[localKey];
        Assert.AreEqual("Genre 2", content.Name, "this device's rename stands");
        CollectionAssert.AreEquivalent(
            action == DataSyncInboxAction.Reapply ? new[] {"Rock", "Jazz", "Pop", "Blues"} : new[] {"Rock", "Jazz", "Blues"},
            content.Children.Select(c => c.Label).ToArray(), "the waiting record was merged once decided");
        var agreed = (await f.BasesAsync(link.Id)).Single(b => b.SyncKey == held.SyncKey);
        Assert.IsNull(agreed.PendingReason);
        Assert.AreEqual(v3, ApplyFixture.Vv(agreed.VvJson!));
        var after = await f.RowAsync(localKey);
        Assert.IsFalse(after.PublishHeld);
        Assert.AreEqual(DataSyncVvRelation.Dominates, ApplyFixture.Vv(after.VvJson).CompareTo(v3));
        Assert.AreEqual(0, (await f.OpenItemsAsync()).Count);
    }

    /// <summary>A resource whose directory name the Regex enhancer reads into <paramref name="propertyId"/>.</summary>
    private static async Task<int> EnhanceableResourceAsync(IServiceProvider services, string root, int propertyId)
    {
        await services.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = root,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory,
            }),
            Priority = 100,
        });
        await services.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
        var resourceId = (await services.GetRequiredService<IResourceService>().GetAll()).Single().Id;
        await services.GetRequiredService<IResourceProfileService>().Add(
            "enhance", "{}", null,
            new ResourceProfileEnhancerOptions
            {
                Enhancers =
                [
                    new EnhancerFullOptions
                    {
                        EnhancerId = (int) EnhancerId.Regex,
                        TargetOptions =
                        [
                            new EnhancerTargetFullOptions
                            {
                                Target = (int) RegexEnhancerTarget.CaptureGroups,
                                DynamicTarget = "studio",
                                PropertyPool = PropertyPool.Custom,
                                PropertyId = propertyId,
                            },
                        ],
                    },
                ],
            },
            null, null, null, 100);
        await services.GetRequiredService<IResourceProfileIndexService>().RebuildAsync(null, CancellationToken.None);
        return resourceId;
    }

    private static async Task<DataSyncRefreshResult> RefreshPropertiesAsync(DataSyncRefreshFixture f)
    {
        using var lease = await f.Gate.EnterAsync(null, default);
        await using var scope = f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DataSyncRefresher>()
            .RefreshAsync(lease, [DataSyncKindIds.CustomProperty], false, default);
    }
}

/// <summary>What the next <c>UpdateRange</c> of custom properties runs first, in every scope; null: nothing.</summary>
internal sealed class CustomPropertyWriteHook
{
    public Func<IReadOnlyCollection<CustomPropertyDbModel>, Task>? BeforeUpdateRange { get; set; }
}

/// <summary>The real custom property service with <see cref="CustomPropertyWriteHook"/> in front of its UpdateRange.</summary>
public class InterceptedCustomProperties : DispatchProxy
{
    private ICustomPropertyService _inner = null!;
    private CustomPropertyWriteHook _hook = null!;

    internal static ICustomPropertyService Wrap(ICustomPropertyService inner, CustomPropertyWriteHook hook)
    {
        var proxy = Create<ICustomPropertyService, InterceptedCustomProperties>();
        var intercepted = (InterceptedCustomProperties) (object) proxy;
        intercepted._inner = inner;
        intercepted._hook = hook;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.Name == nameof(ICustomPropertyService.UpdateRange) && _hook.BeforeUpdateRange is { } before)
            return UpdateRangeAfterAsync(before, method, args);
        return Call(method, args);
    }

    private async Task<BaseResponse> UpdateRangeAfterAsync(Func<IReadOnlyCollection<CustomPropertyDbModel>, Task> before,
        MethodInfo method, object?[]? args)
    {
        await before((IReadOnlyCollection<CustomPropertyDbModel>) args![0]!);
        return await (Task<BaseResponse>) Call(method, args)!;
    }

    private object? Call(MethodInfo method, object?[]? args)
    {
        try
        {
            return method.Invoke(_inner, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(e.InnerException);
            throw;
        }
    }
}

/// <summary>
/// A read-only data sync view of the real custom properties (the kind adapter itself is another package's): each
/// property is <c>{"children":[{id: choice value, label}],"name"}</c> under the memory codec — all Refresh and the
/// lost-update guard read.
/// </summary>
internal sealed class CustomPropertyChoicesKind(ICustomPropertyService properties) : IDataSyncKind
{
    public IDataSyncKindCodec Codec { get; } = new MemoryCodec(DataSyncKindIds.CustomProperty, false);

    private async Task<List<(string LocalKey, JsonObject Content)>> AllAsync()
    {
        var result = new List<(string, JsonObject)>();
        foreach (var property in (await properties.GetAll()).OrderBy(p => p.Id))
        {
            var choices = property.Options switch
            {
                MultipleChoicePropertyOptions m => m.Choices,
                SingleChoicePropertyOptions s => s.Choices,
                _ => null,
            } ?? [];
            result.Add((property.Id.ToString(), new MemoryDefinition(property.Name,
                choices.Select(c => new MemoryChild(c.Value, c.Label)).ToList()).ToContent()));
        }

        return result;
    }

    public async Task<IReadOnlyList<LocalEntity>> ReadAsync(IReadOnlyCollection<string>? localKeys, CancellationToken ct) =>
        (await AllAsync()).Select((e, i) => (e, i)).Where(x => localKeys is null || localKeys.Contains(x.e.LocalKey))
        .Select(x => new LocalEntity(x.e.LocalKey, null, x.i, x.e.Content)).ToList();

    public async Task<IReadOnlyDictionary<string, string>> ReadRawHashesAsync(CancellationToken ct) =>
        (await AllAsync()).ToDictionary(e => e.LocalKey, e => ContentHash.Of(e.Content));

    public Task<IReadOnlyList<string>> ReadOrderAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task<ApplyBatchOutcome> ApplyAsync(ApplyBatch batch, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, JsonObject>> CapturePreImageAsync(IReadOnlyCollection<string> localKeys,
        CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, EntityUsage>> GetUsageAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> childIdsByLocalKey, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task RestoreAsync(string localKey, JsonObject preImage, CancellationToken ct) => throw new NotSupportedException();

    public Task DeleteAsync(string localKey, CancellationToken ct) => throw new NotSupportedException();

    public void ResetCaches()
    {
    }

    public Task ApplyOrderAsync(IReadOnlyList<string> syncedLocalKeysInSharedOrder, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task ChangeSubtypeAsync(string localKey, string subtype, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<DataSyncTypeChangePreview> PreviewSubtypeChangeAsync(string localKey, string subtype,
        CancellationToken ct) => throw new NotSupportedException();
}
