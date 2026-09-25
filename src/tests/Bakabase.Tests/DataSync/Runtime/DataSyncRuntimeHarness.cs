using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.TestKit.Implementations;
using Bootstrap.Components.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.Tests.DataSync.Runtime;

/// <summary>
/// The runtime as <c>AddDataSyncRuntime()</c> composes it, over a real <see cref="BTaskManager"/>, with fakes of
/// packages C and D and a manual clock. Scopes are validated, so a singleton that captured a scoped service fails.
/// </summary>
internal sealed class DataSyncRuntimeHarness : IAsyncDisposable
{
    public ManualDataSyncClock Clock { get; } = new();
    public FakeDataSyncStore Store { get; } = new();
    public FakeDataSyncPeerClient Peers { get; } = new();
    public FakeDataSyncGrantService Grants { get; } = new();
    public FakeReviewStore Reviews { get; } = new();
    public FakeActorGuard Guard { get; } = new();
    public FakeKindPageReader Reader { get; } = new();
    public RecordingObserver Observer { get; } = new();
    public FakeHostLifetime Lifetime { get; } = new();
    public FakeRowTransactions RowTransactions { get; } = new();

    public ServiceProvider Provider { get; private set; } = null!;
    public BTaskManager Btm => Provider.GetRequiredService<BTaskManager>();
    public FakeApplyRunner Runner => (FakeApplyRunner) Provider.GetRequiredService<IDataSyncApplyRunner>();
    public DataSyncScheduler Scheduler => Provider.GetRequiredService<DataSyncScheduler>();
    public DataSyncTaskLauncher Launcher => Provider.GetRequiredService<DataSyncTaskLauncher>();
    public DataSyncLinkService Links => Provider.GetRequiredService<DataSyncLinkService>();
    public DataSyncFetcher Fetcher => Provider.GetRequiredService<DataSyncFetcher>();
    public DataSyncTaskRegistry Registry => (DataSyncTaskRegistry) Provider.GetRequiredService<IDataSyncTaskRegistry>();
    public IDataSyncStagedPullStore StagedPulls => Provider.GetRequiredService<IDataSyncStagedPullStore>();
    public DataSyncGrantEventsHandler GrantEvents => Provider.GetRequiredService<DataSyncGrantEventsHandler>();

    /// <param name="daemon">Run the task manager's daemon, which starts waiting one-shot tasks within a second.</param>
    /// <param name="registerFetchTask">Register the <c>DataSync</c> task as the host does after its migrations.</param>
    public static async Task<DataSyncRuntimeHarness> CreateAsync(bool daemon = false, bool registerFetchTask = true)
    {
        var harness = new DataSyncRuntimeHarness();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IBakabaseLocalizer, TestBakabaseLocalizer>();
        services.AddSingleton<IOptionsMonitor<TaskOptions>>(new TestOptionsMonitor<TaskOptions>(new TaskOptions()));
        services.AddSingleton(sp => new AspNetCoreOptionsManager<TaskOptions>("task", "task",
            sp.GetRequiredService<IOptionsMonitor<TaskOptions>>(),
            sp.GetRequiredService<ILogger<AspNetCoreOptionsManager<TaskOptions>>>()));
        services.AddSingleton<BTaskManager>();
        services.AddSingleton<IBTaskEventHandler, TestBTaskEventHandler>();
        services.AddSingleton<IHostApplicationLifetime>(harness.Lifetime);

        // Seams the runtime registers with TryAdd: the test's win because they come first.
        services.AddSingleton<IDataSyncClock>(harness.Clock);
        services.AddSingleton<IDataSyncRuntimeObserver>(harness.Observer);
        services.AddSingleton<IDataSyncRowTransactions>(harness.RowTransactions);
        services.AddScoped<IDataSyncKindPageReader>(_ => harness.Reader);

        // What packages C and D provide.
        services.AddScoped<IDataSyncStore>(_ => harness.Store);
        services.AddSingleton<IDataSyncPeerClient>(harness.Peers);
        services.AddSingleton<IDataSyncGrantService>(harness.Grants);
        services.AddSingleton<IDataSyncReviewStore>(harness.Reviews);
        services.AddSingleton<IDataSyncActorGuard>(harness.Guard);
        services.AddSingleton<IDataSyncApplyRunner, FakeApplyRunner>();

        services.AddDataSyncRuntime();
        harness.Provider = services.BuildServiceProvider(new ServiceProviderOptions
            {ValidateScopes = true, ValidateOnBuild = true});

        if (daemon) await harness.Btm.Initialize();
        if (registerFetchTask) await harness.RegisterFetchTaskAsync();
        return harness;
    }

    /// <summary>Enqueues the predefined <c>DataSync</c> task exactly as <c>DynamicTaskRegistry</c> does.</summary>
    public async Task RegisterFetchTaskAsync()
    {
        var task = ActivatorUtilities.CreateInstance<DataSyncFetchTask>(Provider);
        Assert.IsTrue(task.IsEnabled());
        await Btm.Enqueue(BTaskBuilder.Create(task.Id, task.GetName)
            .Describe(task.GetDescription)
            .InterruptionMessage(task.GetMessageOnInterruption)
            .AtLevel(task.Level)
            .Persistent(task.IsPersistent)
            .ConflictsWith(task.ConflictKeys)
            .Every(task.GetInterval())
            .Run(task.RunAsync));
    }

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
            FirstContactKindsJson = "[\"extensionGroup\",\"customProperty\"]",
            FirstContactCompletedAtUtc = Clock.UtcNow,
            LastFullReconciliationAtUtc = Clock.UtcNow,
            NextAttemptAtUtc = Clock.UtcNow,
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

    /// <summary>Runs one fetch cycle directly, outside the task manager, with its own cancellation.</summary>
    public Task FetchOnceAsync(CancellationToken ct = default) =>
        FetchOnceAsync(new Bootstrap.Components.Tasks.PauseTokenSource(), ct);

    /// <summary>Runs one fetch cycle with a pause the test controls, as the task manager's pause would.</summary>
    public Task FetchOnceAsync(Bootstrap.Components.Tasks.PauseTokenSource pause, CancellationToken ct = default) =>
        Fetcher.RunCycleAsync(new BTaskArgs(pause.Token, ct,
            new Bakabase.Abstractions.Models.Domain.BTask("test-fetch", () => "test"),
            _ => Task.CompletedTask, Provider));

    public BTaskStatus? Status(string taskId) => Btm.GetTaskViewModel(taskId)?.Status;

    public static async Task WaitUntilAsync(Func<bool> condition, string what, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting until {what}.");
            await Task.Delay(20);
        }
    }

    public Task WaitForStatusAsync(string taskId, BTaskStatus status, int timeoutMs = 10_000) =>
        WaitUntilAsync(() => Status(taskId) == status, $"{taskId} is {status} (now {Status(taskId)})", timeoutMs);

    public async ValueTask DisposeAsync() => await Provider.DisposeAsync();
}
