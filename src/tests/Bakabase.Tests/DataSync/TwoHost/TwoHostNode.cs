using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.TestKit.DataSync;
using Bakabase.TestKit.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync.TwoHost;

/// <summary>
/// One host of the two-host test (§13.7): a TestKit provider with its own SQLite database and data sync folder, the
/// shared clock, its device identity, an <see cref="InProcessPeerClient"/> and the network's grants. Nothing runs by
/// itself: the test drives the scheduler's tick, the fetch task and the write tasks, as the host's own loops would.
/// </summary>
internal sealed class TwoHostNode
{
    private static readonly TimeSpan TaskTimeout = TimeSpan.FromSeconds(60);

    private TwoHostNode(string directory, DataSyncDevice device, InProcessPeerClient client, IServiceProvider services)
    {
        Directory = directory;
        Device = device;
        Client = client;
        Services = services;
    }

    /// <summary>The test directory: <c>test.db</c> and the data sync folder (<c>data-sync/actor.json</c>).</summary>
    public string Directory { get; }

    public DataSyncDevice Device { get; }
    public string NodeId => Device.NodeId;
    public string Name => Device.Name;
    public InProcessPeerClient Client { get; }
    public IServiceProvider Services { get; }

    public BTaskManager Btm => Services.GetRequiredService<BTaskManager>();

    /// <summary>Whether a first sync's snapshot is staged for a link of this node (§8.3).</summary>
    public bool HasPreview(DataSyncLinkDbModel link) =>
        Services.GetRequiredService<DataSyncRuntimeState>().PeekPreview(link.Id) is not null;

    /// <summary>
    /// This node asks <paramref name="approver"/> to keep in step both ways, the approver reads it back, and this node's
    /// first sync is staged (§8.3).
    /// </summary>
    public async Task<DataSyncLinkDbModel> PairTwoWayAsync(TwoHostNode approver, TwoHostNetwork network)
    {
        var created = await CallAsync(s => s.CreateLinkAsync(
            new DataSyncLinkCreateInput(approver.NodeId, null, null, DataSyncLinkMode.TwoWay, []), true, default));
        Assert.IsNull(created.Problem, created.Problem?.Code.ToString());
        var request = (await approver.CallAsync(s => s.GetRequestsAsync(default)))
            .Single(r => r.Direction == DataSyncRequestDirection.Incoming);
        var approved = await approver.CallAsync(s => s.ApproveRequestAsync(request.RequestId,
            new DataSyncApproveInput(true, null), default));
        Assert.IsNull(approved.Problem, approved.Problem?.Code.ToString());
        network.Clock.Advance(TimeSpan.FromSeconds(5));
        network.Claim(NodeId);
        await CycleAsync();
        var link = await RequireLinkToAsync(approver);
        Assert.IsTrue(HasPreview(link), $"no first sync staged: {link.State} {link.LastErrorCode}");
        return link;
    }

    /// <summary>Starts a link's first sync as the page does, and runs it.</summary>
    public async Task StartFirstSyncAsync(DataSyncLinkDbModel link, params DataSyncFirstSyncChoice[] choices) =>
        await RunAsync(await CallAsync(s => s.StartFirstSyncAsync(link.Id, new DataSyncFirstSyncStartInput(choices),
            default)));

    /// <summary>Answers every open link suggestion with a link to its only candidate, as the page's bulk action does.</summary>
    public async Task LinkEverySuggestionAsync()
    {
        var suggestions = (await CallAsync(s => s.GetInboxAsync(new DataSyncInboxQuery(), default))).Items
            .Where(i => i.Type == DataSyncInboxItemType.LinkSuggestion).ToList();
        await RunAsync(await CallAsync(s => s.ResolveAsync(new DataSyncResolveBatchInput(suggestions.Select(i =>
            new DataSyncResolveInput(i.Id, DataSyncInboxAction.Link, i.Token, null,
                i.Payload.Candidates!.Single().LocalKey, null, null)).ToList(), false), default)));
    }
    public DataSyncScheduler Scheduler => Services.GetRequiredService<DataSyncScheduler>();
    public DataSyncActorGuard Guard => Services.GetRequiredService<DataSyncActorGuard>();

    /// <summary>
    /// Starts a host over <paramref name="directory"/> (a fresh one when null), registers the <c>DataSync</c> task as
    /// the host does after its migrations, and attaches it to the network in place of an earlier process of the same
    /// device. <paramref name="headless"/>: a NAS or Docker server, which notifies nobody.
    /// </summary>
    public static async Task<TwoHostNode> StartAsync(TwoHostNetwork network, DataSyncDevice device,
        string? directory = null, bool headless = false)
    {
        directory ??= TestServiceBuilder.NewTestDirectory();
        var client = new InProcessPeerClient();
        var services = await TestServiceBuilder.BuildServiceProvider(directory, s =>
        {
            s.AddSingleton<TimeProvider>(network.Clock);
            s.AddSingleton<IDataSyncClock>(network.Clock);
            s.AddSingleton<IDataSyncDeviceIdentity>(new TestDataSyncDeviceIdentity(device));
            s.AddSingleton<IDataSyncPeerClient>(client);
            s.AddSingleton(network.GrantsFor(device.NodeId));
            s.AddSingleton<IDataSyncHostKind>(new TestDataSyncHostKind { IsHeadless = headless });
        });
        var node = new TwoHostNode(directory, device, client, services);
        var task = ActivatorUtilities.CreateInstance<DataSyncFetchTask>(services);
        await node.Btm.Enqueue(BTaskBuilder.Create(task.Id, task.GetName)
            .Describe(task.GetDescription)
            .InterruptionMessage(task.GetMessageOnInterruption)
            .AtLevel(task.Level)
            .Persistent(task.IsPersistent)
            .ConflictsWith(task.ConflictKeys)
            .Every(task.GetInterval())
            .Run(task.RunAsync));
        network.Attach(node);
        return node;
    }

    // ---- scopes ------------------------------------------------------------------------------------------------

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> body)
    {
        await using var scope = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        return await body(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> body) => InScopeAsync(async sp =>
    {
        await body(sp);
        return 0;
    });

    /// <summary>A call of the <c>/data-sync</c> facade, in a request scope of its own.</summary>
    public Task<T> CallAsync<T>(Func<IDataSyncService, Task<T>> call) =>
        InScopeAsync(sp => call(sp.GetRequiredService<IDataSyncService>()));

    public Task<T> DbAsync<T>(Func<BakabaseDbContext, Task<T>> read) =>
        InScopeAsync(sp => read(sp.GetRequiredService<BakabaseDbContext>()));

    // ---- driving the runtime -----------------------------------------------------------------------------------

    /// <summary>One scheduler tick (§8.2): grant events, verification, the fetch task and the apply task.</summary>
    public Task TickAsync() => Scheduler.TickAsync(CancellationToken.None);

    /// <summary>
    /// A tick, then everything it started until nothing waits: the fetch task, and every data sync write task it or
    /// the tick enqueued.
    /// </summary>
    public async Task CycleAsync()
    {
        await TickAsync();
        await WaitForFetchAsync();
        await RunWriteTasksAsync();
        await TickAsync();
        await WaitForFetchAsync();
        await RunWriteTasksAsync();
    }

    public async Task WaitForFetchAsync()
    {
        var deadline = DateTime.UtcNow + TaskTimeout;
        while (Btm.GetTaskViewModel(DataSyncTaskIds.Fetch) is { } task && task.Status.IsActive())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail($"{Name}: the DataSync task did not finish.");
            await Task.Delay(5);
        }

        if (Btm.GetTaskViewModel(DataSyncTaskIds.Fetch) is { Status: BTaskStatus.Error } failed)
            Assert.Fail($"{Name}: the DataSync task failed: {failed.Error}");
    }

    /// <summary>
    /// Starts every data sync write task that waits and waits for it, until none waits or runs (the daemon's job in
    /// a host). A task that ends in an error fails the test, unless <paramref name="mayFail"/> takes its id.
    /// </summary>
    public async Task RunWriteTasksAsync(Func<string, bool>? mayFail = null)
    {
        for (var round = 0; round < 50; round++)
        {
            var waiting = Btm.Tasks
                .Where(t => DataSyncTaskIds.IsWriteTask(t.Id) && t.Task.Status == BTaskStatus.NotStarted)
                .Select(t => t.Id).ToList();
            if (waiting.Count == 0 && !Btm.Tasks.Any(t => DataSyncTaskIds.IsWriteTask(t.Id) && t.Task.Status.IsActive()))
                return;
            foreach (var id in waiting) await Btm.Start(id);
            var deadline = DateTime.UtcNow + TaskTimeout;
            while (Btm.Tasks.Any(t => DataSyncTaskIds.IsWriteTask(t.Id) && t.Task.Status.IsActive()) ||
                   waiting.Any(id => Btm.GetTaskViewModel(id)?.Status == BTaskStatus.NotStarted))
            {
                if (DateTime.UtcNow > deadline) Assert.Fail($"{Name}: data sync write tasks did not finish.");
                await Task.Delay(5);
            }

            foreach (var id in waiting)
            {
                if (Btm.GetTaskViewModel(id) is { Status: BTaskStatus.Error } failed && mayFail?.Invoke(id) != true)
                    Assert.Fail($"{Name}: {id} failed: {failed.BriefError ?? failed.Error}");
            }
        }

        Assert.Fail($"{Name}: data sync write tasks kept enqueueing each other.");
    }

    /// <summary>Starts a write task the facade answered with, and waits until it and anything it enqueued ran.</summary>
    public async Task RunAsync(DataSyncTaskStart start)
    {
        Assert.IsNull(start.Problem, $"{Name}: {start.Problem?.Code} {start.Problem?.Detail}");
        Assert.IsNotNull(start.TaskId);
        await RunWriteTasksAsync();
    }

    // ---- reading state -----------------------------------------------------------------------------------------

    public Task<DataSyncLocalStateDbModel> LocalStateAsync() =>
        DbAsync(db => db.DataSyncLocalStates.AsNoTracking().SingleAsync());

    public Task<DataSyncLinkDbModel?> LinkToAsync(TwoHostNode peer) =>
        DbAsync(db => db.DataSyncLinks.AsNoTracking().SingleOrDefaultAsync(l => l.PeerNodeId == peer.NodeId));

    public async Task<DataSyncLinkDbModel> RequireLinkToAsync(TwoHostNode peer) =>
        await LinkToAsync(peer) ?? throw new AssertFailedException($"{Name} has no link to {peer.Name}.");

    public Task<List<DataSyncEntityDbModel>> EntitiesAsync(bool includeTombstones = false) =>
        DbAsync(db => db.DataSyncEntities.AsNoTracking()
            .Where(e => includeTombstones || e.DeletedAtUtc == null).OrderBy(e => e.Id).ToListAsync());

    public Task<List<DataSyncInboxItemDbModel>> ItemsAsync(bool openOnly = true) =>
        DbAsync(db => db.DataSyncInboxItems.AsNoTracking().Where(i => !openOnly || i.ClosedAtUtc == null)
            .OrderBy(i => i.Id).ToListAsync());

    public Task<List<DataSyncApplyLogDbModel>> HistoryAsync() =>
        DbAsync(db => db.DataSyncApplyLogs.AsNoTracking().OrderBy(l => l.Id).ToListAsync());

    /// <summary>Every key of every row (primaries and aliases), by kind.</summary>
    public Task<Dictionary<(string Kind, string Key), long>> KeysAsync() => DbAsync(async db =>
    {
        var result = new Dictionary<(string, string), long>();
        foreach (var row in await db.DataSyncEntities.AsNoTracking().ToListAsync()) result[(row.Kind, row.SyncKey)] = row.Id;
        foreach (var alias in await db.DataSyncKeyAliases.AsNoTracking().ToListAsync())
        {
            var owner = await db.DataSyncEntities.AsNoTracking()
                .SingleAsync(e => e.Kind == alias.Kind && e.SyncKey == alias.SyncKey);
            result[(alias.Kind, alias.AliasKey)] = owner.Id;
        }

        return result;
    });

    // ---- copies of the data directory (§13.7 step 8) -----------------------------------------------------------

    public string DatabasePath => Path.Combine(Directory, "test.db");

    public string DataSyncFolder => Services.GetRequiredService<IDataSyncDataDirectory>().Path;

    /// <summary>A consistent copy of the database file (<c>VACUUM INTO</c>), as a backup of the database alone.</summary>
    public async Task<string> CopyDatabaseAsync(string target)
    {
        if (File.Exists(target)) File.Delete(target);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false}.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $target";
        command.Parameters.AddWithValue("$target", target);
        await command.ExecuteNonQueryAsync();
        return target;
    }

    /// <summary>The whole data directory as a backup of it: the database and the data sync folder.</summary>
    public async Task<string> CopyDataDirectoryAsync(string target)
    {
        System.IO.Directory.CreateDirectory(target);
        await CopyDatabaseAsync(Path.Combine(target, "test.db"));
        CopyFolder(DataSyncFolder, Path.Combine(target, "data-sync"));
        return target;
    }

    public static void CopyFolder(string from, string to)
    {
        System.IO.Directory.CreateDirectory(to);
        foreach (var file in System.IO.Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
    }
}
