using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.TestKit.DataSync;
using Bakabase.TestKit.Implementations;
using Bakabase.Tests.DataSync.TwoHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests.DataSync.Convergence;

/// <summary>
/// The hosts of one test on the real stack (§13.3 over §13.7's harness): each a <see cref="TwoHostNode"/> with its own
/// SQLite database, data sync folder and runtime, reading the others' real feeds over <see cref="TwoHostNetwork"/>,
/// all on one clock. The test acts as people and the hosts' own loops would: definitions through the Property and
/// extension group services, values as resources would hold them, and every data sync action through the facade.
/// </summary>
internal sealed class SyncWorld
{
    public SyncWorld() => Network = new TwoHostNetwork(Clock);

    public DataSyncTestClock Clock { get; } =
        new(DateTime.UtcNow.AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond)));

    public TwoHostNetwork Network { get; }
    public List<SyncHost> Hosts { get; } = [];

    public async Task<SyncHost> AddHostAsync(string name, bool headless = false)
    {
        var device = new DataSyncDevice(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), name);
        var host = new SyncHost(this, await TwoHostNode.StartAsync(Network, device, headless: headless), headless);
        Hosts.Add(host);
        return host;
    }

    /// <summary>
    /// <paramref name="reader"/> asks to receive from <paramref name="source"/> (two-way: both ways), the source
    /// approves, and both finish their first contact: the reader's first sync started and its name matches linked, the
    /// approver's first pull.
    /// </summary>
    public async Task LinkAsync(SyncHost reader, SyncHost source, DataSyncLinkMode mode)
    {
        var created = await reader.CallAsync(s => s.CreateLinkAsync(
            new DataSyncLinkCreateInput(source.NodeId, null, null, mode, [.. DataSyncKindIds.All]), true, default));
        Assert.IsNull(created.Problem, $"{reader} → {source}: {created.Problem?.Code} {created.Problem?.Detail}");
        if (created.RequestId is { } requestId)
        {
            var approved = await source.CallAsync(s => s.ApproveRequestAsync(requestId,
                new DataSyncApproveInput(mode == DataSyncLinkMode.TwoWay, null), default));
            Assert.IsNull(approved.Problem, $"{source} approves {reader}: {approved.Problem?.Code}");
            Network.Claim(reader.NodeId);
        }

        await SyncAsync(reader);
        await reader.StartFirstSyncAsync(source);
        await SyncAsync(source);
        await SyncAsync(reader);
    }

    /// <summary>A pull by <paramref name="host"/>: every link of it is due again a minute later (§8.2).</summary>
    public async Task SyncAsync(SyncHost host)
    {
        Clock.Advance(TimeSpan.FromSeconds(61));
        await host.Node.CycleAsync();
    }

    public async Task SyncAllAsync()
    {
        foreach (var host in Hosts) await SyncAsync(host);
    }

    /// <summary>What every host holds: what a round that changes nothing leaves as it was.</summary>
    public async Task<string> DigestAsync(bool detail = false)
    {
        var parts = new List<string>();
        foreach (var host in Hosts) parts.Add(await host.DigestAsync(detail));
        return string.Join("\n", parts);
    }
}

/// <summary>One host of a <see cref="SyncWorld"/>, and what a person does there.</summary>
internal sealed class SyncHost(SyncWorld world, TwoHostNode node, bool headless)
{
    private int _nextResource = 1;

    public TwoHostNode Node { get; private set; } = node;
    public bool Headless { get; } = headless;
    public string Name => Node.Name;
    public string NodeId => Node.NodeId;

    public override string ToString() => Name;

    public Task<T> CallAsync<T>(Func<IDataSyncService, Task<T>> call) => Node.CallAsync(call);

    /// <summary>Starts a write task the facade answered with and runs what it enqueued.</summary>
    public async Task RunAsync(Func<IDataSyncService, Task<DataSyncTaskStart>> call)
    {
        var start = await CallAsync(call);
        Assert.IsNull(start.Problem, $"{Name}: {start.Problem?.Code} {start.Problem?.Detail}");
        await Node.RunWriteTasksAsync();
    }

    // ---- definitions ---------------------------------------------------------------------------------------------

    public Task<List<CustomProperty>> PropertiesAsync() =>
        Node.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyService>().GetAll());

    public async Task<CustomProperty> PropertyAsync(string name) =>
        (await PropertiesAsync()).Single(p => p.Name == name);

    public Task<ExtensionGroup[]> GroupsAsync() =>
        Node.InScopeAsync(sp => sp.GetRequiredService<IExtensionGroupService>().GetAll());

    public Task<CustomProperty> AddPropertyAsync(string name, PropertyType type, params string[] labels) =>
        Node.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyService>().Add(new CustomPropertyAddOrPutDto
        {
            Name = name, Type = type,
            Options = SyncOptions.Write(type, false, labels.Select(l => new SyncOption(NewId(), l)).ToList()),
        }));

    /// <summary>Saves a property as the property page does: name, type and options as given.</summary>
    public Task PutAsync(CustomProperty property, string name, bool ignoreCase, IReadOnlyList<SyncOption> options) =>
        Node.InScopeAsync(sp => sp.GetRequiredService<ICustomPropertyService>().Put(property.Id,
            new CustomPropertyAddOrPutDto
            {
                Name = name, Type = property.Type, Options = SyncOptions.Write(property.Type, ignoreCase, options),
            }));

    public async Task EditAsync(string name, Func<List<SyncOption>, IEnumerable<SyncOption>> edit, string? rename = null) =>
        await EditAsync(await PropertyAsync(name), edit, rename);

    public Task EditAsync(CustomProperty property, Func<List<SyncOption>, IEnumerable<SyncOption>> edit,
        string? rename = null) =>
        PutAsync(property, rename ?? property.Name, SyncOptions.IgnoreCase(property), edit(SyncOptions.Read(property)).ToList());

    public Task RenameAsync(string name, string to) => EditAsync(name, o => o, to);

    public Task ChangeTypeAsync(CustomProperty property, PropertyType type) => Node.InScopeAsync(sp =>
        sp.GetRequiredService<ICustomPropertyService>().ChangeType(property.Id, type));

    public Task DeletePropertyAsync(CustomProperty property) => Node.InScopeAsync(sp =>
        sp.GetRequiredService<ICustomPropertyService>().RemoveByKey(property.Id));

    public Task AddGroupAsync(string name, params string[] extensions) => Node.InScopeAsync(sp =>
        sp.GetRequiredService<IExtensionGroupService>().Add(new ExtensionGroupAddInputModel(name, [.. extensions])));

    public Task PutGroupAsync(ExtensionGroup group, string name, IEnumerable<string> extensions) =>
        Node.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IExtensionGroupService>().Put(group.Id,
                new ExtensionGroupPutInputModel(name, [.. extensions]));
            return 0;
        });

    public Task DeleteGroupAsync(ExtensionGroup group) => Node.InScopeAsync(async sp =>
    {
        await sp.GetRequiredService<IExtensionGroupService>().Delete(group.Id);
        return 0;
    });

    /// <summary><paramref name="resources"/> resources here hold the option as their value (§8.5.4 usage).</summary>
    public Task UseAsync(CustomProperty property, string optionId, int resources = 1) => Node.InScopeAsync(sp =>
        sp.GetRequiredService<ICustomPropertyValueService>().AddDbModelRange(Enumerable.Range(0, resources).Select(_ =>
            new CustomPropertyValueDbModel
            {
                ResourceId = _nextResource++, PropertyId = property.Id, Scope = (int) PropertyValueScope.Manual,
                Value = property.Type == PropertyType.SingleChoice
                    ? optionId.SerializeAsStandardValue(StandardValueType.String)
                    : new List<string> {optionId}.SerializeAsStandardValue(StandardValueType.ListString),
            }).ToList()));

    /// <summary>Every option id resources here use, by property.</summary>
    public Task<Dictionary<int, HashSet<string>>> UsageAsync() => Node.DbAsync(async db =>
    {
        var usage = new Dictionary<int, HashSet<string>>();
        foreach (var value in await db.CustomPropertyValues.AsNoTracking().ToListAsync())
        {
            if (!usage.TryGetValue(value.PropertyId, out var ids)) usage[value.PropertyId] = ids = [];
            if (value.Value?.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString) is { } list)
                ids.UnionWith(list);
        }

        return usage;
    });

    public static string NewId() => Guid.NewGuid().ToString();

    // ---- data sync -----------------------------------------------------------------------------------------------

    /// <summary>The open items, open ones only, as the page shows them.</summary>
    public async Task<IReadOnlyList<DataSyncInboxItemView>> OpenItemsAsync() =>
        (await CallAsync(s => s.GetInboxAsync(new DataSyncInboxQuery(Take: 5000), default))).Items;

    /// <summary>
    /// Decides one item; every other open conflict of its definition keeps this device's value in the same batch, as
    /// the facade takes the conflicts of an entity only together (§9.2).
    /// </summary>
    public async Task<DataSyncProblem?> ResolveAsync(DataSyncInboxItemView item, DataSyncInboxAction action,
        string? target = null, string? custom = null)
    {
        var inputs = new List<DataSyncResolveInput>
        {
            new(item.Id, action, item.Token, custom, action == DataSyncInboxAction.KeepRecordLinked ? null : target,
                action == DataSyncInboxAction.KeepRecordLinked ? target : null, null),
        };
        if (item.LocalKey is not null && Bakabase.InsideWorld.Business.Components.DataSync.Runtime.DataSyncInboxRules.IsConflict(item.Type))
        {
            inputs.AddRange((await OpenItemsAsync())
                .Where(i => i.Id != item.Id && i.Kind == item.Kind && i.LocalKey == item.LocalKey &&
                            Bakabase.InsideWorld.Business.Components.DataSync.Runtime.DataSyncInboxRules.IsConflict(i.Type))
                .Select(i => new DataSyncResolveInput(i.Id, DataSyncInboxAction.KeepLocal, i.Token, null, null, null,
                    null)));
        }

        var start = await CallAsync(s => s.ResolveAsync(new DataSyncResolveBatchInput(inputs, false), default));
        await Node.RunWriteTasksAsync();
        return start.Problem;
    }

    /// <summary>
    /// The first sync staged for this host's link to <paramref name="source"/>, started as previewed, and every link
    /// suggestion it raised answered with a link to a candidate no other answer took, else by keeping both; nothing
    /// while none is staged (a cut-off source's is fetched by a later cycle).
    /// </summary>
    public async Task StartFirstSyncAsync(SyncHost source)
    {
        var link = await Node.RequireLinkToAsync(source.Node);
        if (link.State != DataSyncLinkState.AwaitingReview || !Node.HasPreview(link)) return;
        await Node.StartFirstSyncAsync(link);
        var used = new HashSet<(string, string)>();
        foreach (var item in (await OpenItemsAsync()).Where(i =>
                     i.LinkId == link.Id && i.Type == DataSyncInboxItemType.LinkSuggestion))
        {
            var target = item.Payload.Candidates?.Select(c => c.LocalKey)
                .FirstOrDefault(k => used.Add((item.Kind, k)));
            await ResolveAsync(item, target is null ? DataSyncInboxAction.KeepBoth : DataSyncInboxAction.Link, target);
        }
    }

    public Task<DataSyncLocalStateDbModel> LocalStateAsync() => Node.LocalStateAsync();

    /// <summary>A copy of the database alone (§13.7 step 8), to restore later.</summary>
    public Task<string> BackupDatabaseAsync() =>
        Node.CopyDatabaseAsync(Path.Combine(Node.Directory, $"backup-{Guid.NewGuid():N}.db"));

    /// <summary>
    /// The database goes back to <paramref name="backup"/>; <c>actor.json</c> stays, and the host restarts: its
    /// scheduler's first tick checks the actor, as a started host's does at once (§5.6).
    /// </summary>
    public async Task RestoreDatabaseAsync(string backup)
    {
        var directory = Bakabase.TestKit.Utils.TestServiceBuilder.NewTestDirectory();
        Directory.CreateDirectory(directory);
        File.Copy(backup, Path.Combine(directory, "test.db"));
        TwoHostNode.CopyFolder(Node.DataSyncFolder, Path.Combine(directory, "data-sync"));
        Node = await TwoHostNode.StartAsync(world.Network, Node.Device, directory, Headless);
        await Node.TickAsync();
    }

    /// <param name="detail">Every item, entity and pending record too: what a failing seed's report shows.</param>
    public async Task<string> DigestAsync(bool detail = false)
    {
        var state = await Node.LocalStateAsync();
        var links = await Node.DbAsync(db => db.DataSyncLinks.AsNoTracking().OrderBy(l => l.Id).ToListAsync());
        var items = await Node.ItemsAsync(!detail);
        var digest = $"{Name}: seq {state.LastSeq} actor {state.ActorId}:{state.ActorCounter} restore {state.RestoreReason} " +
                     $"links [{string.Join(", ", links.Select(l => $"{l.PeerName}:{l.State}:{l.PausedReason}:{l.CursorsJson}"))}] " +
                     $"items [{string.Join(", ", items.Select(i => $"{i.Type}:{i.SubjectPath}" +
                         (detail ? $":{i.Kind}/{i.LocalKey}/{i.SyncKey[..8]}:{i.Closure}:{i.Action}" : "")))}]";
        if (!detail) return digest;
        var entities = await Node.EntitiesAsync(true);
        var bases = await Node.DbAsync(db => db.DataSyncPeerBases.AsNoTracking().ToListAsync());
        return digest + $"\n    entities [{string.Join(", ", entities.Select(e =>
                   $"{e.Kind}/{e.LocalKey}/{e.SyncKey[..8]}:{e.State}{(e.DeletedAtUtc is null ? "" : ":deleted")}"))}]" +
               $"\n    bases [{string.Join(", ", bases.Select(b => $"{b.LinkId}/{b.Kind}/{b.SyncKey[..8]}:{b.State}:" +
                   $"{b.ExclusionReason}:{b.PendingReason}"))}]";
    }
}

/// <summary>An option of a choice, tags or multilevel property, flattened (a multilevel node names its parent).</summary>
internal sealed record SyncOption(string Id, string Label, string? Color = null, string? Group = null,
    string? Parent = null);

internal static class SyncOptions
{
    public static readonly PropertyType[] WithOptions =
        [PropertyType.SingleChoice, PropertyType.MultipleChoice, PropertyType.Tags, PropertyType.Multilevel];

    private static T? Json<T>(object? options) => options is null
        ? default
        : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(options));

    public static bool IgnoreCase(CustomProperty p) => p.Type switch
    {
        PropertyType.SingleChoice or PropertyType.MultipleChoice => Json<MultipleChoicePropertyOptions>(p.Options)
            ?.IgnoreCase == true,
        PropertyType.Tags => Json<TagsPropertyOptions>(p.Options)?.IgnoreCase == true,
        PropertyType.Multilevel => Json<MultilevelPropertyOptions>(p.Options)?.IgnoreCase == true,
        _ => false,
    };

    public static List<SyncOption> Read(CustomProperty p) => p.Type switch
    {
        PropertyType.SingleChoice or PropertyType.MultipleChoice => Json<MultipleChoicePropertyOptions>(p.Options)
            ?.Choices?.Select(c => new SyncOption(c.Value, c.Label, c.Color)).ToList() ?? [],
        PropertyType.Tags => Json<TagsPropertyOptions>(p.Options)?.Tags
            ?.Select(t => new SyncOption(t.Value, t.Name, t.Color, t.Group)).ToList() ?? [],
        PropertyType.Multilevel => Flatten(Json<MultilevelPropertyOptions>(p.Options)?.Data, null).ToList(),
        _ => [],
    };

    private static IEnumerable<SyncOption> Flatten(IEnumerable<MultilevelDataOptions>? nodes, string? parent) =>
        (nodes ?? []).SelectMany(n => Flatten(n.Children, n.Value)
            .Prepend(new SyncOption(n.Value, n.Label, string.IsNullOrEmpty(n.Color) ? null : n.Color, null, parent)));

    public static string? Write(PropertyType type, bool ignoreCase, IReadOnlyList<SyncOption> options)
    {
        List<MultilevelDataOptions> Tree(string? parent) => options.Where(o => o.Parent == parent)
            .Select(o => new MultilevelDataOptions
                { Value = o.Id, Label = o.Label, Color = o.Color!, Children = Tree(o.Id) is { Count: > 0 } c ? c : null })
            .ToList();
        object? written = type switch
        {
            PropertyType.SingleChoice => new SingleChoicePropertyOptions
                { IgnoreCase = ignoreCase, Choices = Choices(options) },
            PropertyType.MultipleChoice => new MultipleChoicePropertyOptions
                { IgnoreCase = ignoreCase, Choices = Choices(options) },
            PropertyType.Tags => new TagsPropertyOptions
            {
                IgnoreCase = ignoreCase,
                Tags = options.Select(o => new TagsPropertyOptions.TagOptions(o.Group, o.Label)
                    { Value = o.Id, Color = o.Color }).ToList(),
            },
            PropertyType.Multilevel => new MultilevelPropertyOptions { IgnoreCase = ignoreCase, Data = Tree(null) },
            _ => null,
        };
        return written is null ? null : JsonConvert.SerializeObject(written);
    }

    private static List<ChoiceOptions> Choices(IEnumerable<SyncOption> options) =>
        options.Select(o => new ChoiceOptions { Value = o.Id, Label = o.Label, Color = o.Color }).ToList();
}
