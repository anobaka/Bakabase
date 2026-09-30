using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.TestKit.DataSync;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>
/// The runtime and the facade as the host composes them: a TestKit provider over real SQLite, with the real store,
/// refresher and kinds. Faked are only the parts a test plays or breaks: the peers, the grant service,
/// the actor guard, the apply runner and the page reader. One clock moves the runtime and the persistence layer.
/// </summary>
internal abstract class DataSyncHarness : IAsyncDisposable
{
    public static readonly DataSyncDevice Self = new("node-self", "epoch-self", "This PC");

    private readonly DataSyncWriteCounter _writes = new();

    public DataSyncTestClock Clock { get; } = new(new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
    public DataSyncTestStore Store { get; private set; } = null!;
    public FakeDataSyncPeerClient Peers { get; } = new();
    public FakeDataSyncGrantService Grants { get; } = new();
    public FakeActorGuard Guard { get; } = new();
    public FakeKindPageReader Reader { get; } = new();
    public FakeHostLifetime Lifetime { get; } = new();
    public IServiceProvider Provider { get; private set; } = null!;

    public BTaskManager Btm => Provider.GetRequiredService<BTaskManager>();
    public FakeApplyRunner Runner => (FakeApplyRunner) Provider.GetRequiredService<IDataSyncApplyRunner>();
    public DataSyncScheduler Scheduler => Provider.GetRequiredService<DataSyncScheduler>();
    public DataSyncTaskLauncher Launcher => Provider.GetRequiredService<DataSyncTaskLauncher>();
    public DataSyncLinkService Links => Provider.GetRequiredService<DataSyncLinkService>();
    public DataSyncFetcher Fetcher => Provider.GetRequiredService<DataSyncFetcher>();
    public DataSyncRuntimeState State => Provider.GetRequiredService<DataSyncRuntimeState>();
    public DataSyncTaskRegistry Registry => Provider.GetRequiredService<DataSyncTaskRegistry>();
    public DataSyncGrantEventsHandler GrantEvents => Provider.GetRequiredService<DataSyncGrantEventsHandler>();

    /// <summary>This device's actor, as the local state row has it.</summary>
    public string SelfActor => Store.LocalState!.ActorId;

    /// <param name="harness">The harness's own registrations; <paramref name="configure"/> comes after them.</param>
    protected static async Task<T> BuildAsync<T>(T h, bool registerFetchTask, Action<IServiceCollection> harness,
        Action<IServiceCollection>? configure) where T : DataSyncHarness
    {
        h.Grants.Now = () => h.Clock.UtcNow;
        h.Provider = await TestServiceBuilder.BuildServiceProvider(s =>
        {
            s.ConfigureDbContext<BakabaseDbContext>(o => o.AddInterceptors(h._writes));
            s.AddSingleton<TimeProvider>(h.Clock);
            s.AddSingleton<IDataSyncClock>(h.Clock);
            s.AddSingleton<IHostApplicationLifetime>(h.Lifetime);
            s.AddSingleton<IDataSyncDeviceIdentity>(new TestDataSyncDeviceIdentity(Self));
            s.AddSingleton<IDataSyncPeerClient>(h.Peers);
            s.AddSingleton<IDataSyncGrantService>(h.Grants);
            s.AddSingleton<IDataSyncActorGuard>(h.Guard);
            s.AddSingleton<IDataSyncApplyRunner, FakeApplyRunner>();
            s.AddScoped<IDataSyncKindPageReader>(_ => h.Reader);
            harness(s);
            configure?.Invoke(s);
        });
        h.Store = new DataSyncTestStore(h.Provider, h._writes);
        var device = await h.Provider.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(default);
        h.Store.LocalState = DataSyncLocalStateRows.New(device, h.Clock.UtcNow);
        if (registerFetchTask) await h.RegisterFetchTaskAsync();
        return h;
    }

    /// <summary>Enqueues the predefined <c>DataSync</c> task exactly as <c>DynamicTaskRegistry</c> does.</summary>
    public async Task RegisterFetchTaskAsync()
    {
        var task = ActivatorUtilities.CreateInstance<DataSyncFetchTask>(Provider);
        await Btm.Enqueue(BTaskBuilder.Create(task.Id, task.GetName)
            .Describe(task.GetDescription)
            .InterruptionMessage(task.GetMessageOnInterruption)
            .AtLevel(task.Level)
            .Persistent(task.IsPersistent)
            .ConflictsWith(task.ConflictKeys)
            .Every(task.GetInterval())
            .Run(task.RunAsync));
    }

    /// <summary>A two-way link past its first contact, with cursors 3 and 5, and access to its (fake) peer.</summary>
    public DataSyncLinkDbModel AddLink(string peerNodeId, Action<DataSyncLinkDbModel>? configure = null)
    {
        var link = new DataSyncLinkDbModel
        {
            PeerNodeId = peerNodeId,
            PeerName = "Peer " + peerNodeId,
            Mode = DataSyncLinkMode.TwoWay,
            State = DataSyncLinkState.Active,
            Initiator = DataSyncLinkInitiator.ThisDevice,
            PeerLibraryEpoch = "epoch-1",
            PeerActorId = "0123456789abcdef",
            PeerContractVersion = Bakabase.Modules.DataSync.Wire.DataSyncContract.Version,
            FirstContactCompletedAtUtc = Clock.UtcNow,
            LastFullReconciliationAtUtc = Clock.UtcNow,
            LastSyncedAtUtc = Clock.UtcNow,
            CreatedAtUtc = Clock.UtcNow,
            UpdatedAtUtc = Clock.UtcNow,
        };
        link.SetCursors(new Dictionary<string, long> {["extensionGroup"] = 3, ["customProperty"] = 5});
        configure?.Invoke(link);
        Grants.Outbound.Add(peerNodeId);
        if (!Peers.Peers.ContainsKey(peerNodeId)) Peers.Add(peerNodeId);
        return Store.Add(link);
    }

    public DataSyncLinkDbModel Link(int id) => Store.Get(id) ?? throw new AssertFailedException($"No link {id}.");

    public static async Task WaitUntilAsync(Func<bool> condition, string what, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting until {what}.");
            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Btm.DisposeAsync();
        if (Provider is IAsyncDisposable scope) await scope.DisposeAsync();
    }
}

/// <summary>The runtime on its own: the observer records what it hears instead of notifying anyone.</summary>
internal sealed class DataSyncRuntimeHarness : DataSyncHarness
{
    public RecordingObserver Observer { get; } = new();
    public FakeRowTransactions RowTransactions { get; } = new();

    /// <param name="daemon">Run the task manager's daemon, which starts waiting one-shot tasks within a second.</param>
    /// <param name="registerFetchTask">Register the <c>DataSync</c> task as the host does after its migrations.</param>
    /// <param name="heldRowTransactions">
    /// Row transactions the test holds (<see cref="RowTransactions"/>) instead of the database's own.
    /// </param>
    /// <param name="configure">More registrations, made after the harness's own.</param>
    public static async Task<DataSyncRuntimeHarness> CreateAsync(bool daemon = false, bool registerFetchTask = true,
        bool heldRowTransactions = false, Action<IServiceCollection>? configure = null)
    {
        var h = new DataSyncRuntimeHarness();
        await BuildAsync(h, registerFetchTask, s =>
        {
            s.AddSingleton<IDataSyncRuntimeObserver>(h.Observer);
            if (heldRowTransactions) s.AddSingleton<IDataSyncRowTransactions>(h.RowTransactions);
        }, configure);
        if (daemon) await h.Btm.Initialize();
        return h;
    }

    /// <summary>Runs one fetch cycle directly, outside the task manager, with its own cancellation.</summary>
    public Task FetchOnceAsync(CancellationToken ct = default) =>
        FetchOnceAsync(new Bootstrap.Components.Tasks.PauseTokenSource(), ct);

    /// <summary>Runs one fetch cycle with a pause the test controls, as the task manager's pause would.</summary>
    public Task FetchOnceAsync(Bootstrap.Components.Tasks.PauseTokenSource pause, CancellationToken ct = default) =>
        Fetcher.RunCycleAsync(new BTaskArgs(pause.Token, ct,
            new Bakabase.Abstractions.Models.Domain.BTask("test-fetch", () => "test"),
            _ => Task.CompletedTask, Provider));

    public BTaskStatus? Status(string taskId) => Btm.GetTaskViewModel(taskId)?.Status;

    public Task WaitForStatusAsync(string taskId, BTaskStatus status, int timeoutMs = 10_000) =>
        WaitUntilAsync(() => Status(taskId) == status, $"{taskId} is {status} (now {Status(taskId)})", timeoutMs);
}
