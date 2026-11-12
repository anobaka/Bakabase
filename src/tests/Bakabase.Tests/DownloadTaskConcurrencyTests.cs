using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadTaskConcurrencyTests
{
    private IServiceProvider _services = null!;
    private DownloadTaskService _service = null!;
    private DownloaderManager _manager = null!;
    private readonly Policy _policy = new();
    private readonly Factory _factory = new();
    private string _directory = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _directory = TestServiceBuilder.NewTestDirectory();
        _services = await TestServiceBuilder.BuildServiceProvider(_directory, services =>
        {
            services.AddSingleton<IDownloaderFactory>(_factory);
            services.RemoveAll<IDownloadTaskConcurrencyPolicy>();
            services.AddSingleton<IDownloadTaskConcurrencyPolicy>(_policy);
            services.RemoveAll<IDownloadTaskPrecheck>();
            services.AddSingleton(new DownloadStorageAdmission(_ => _directory, _ => long.MaxValue, _ => { }));
            services.AddSingleton<DownloadQueuePump, NoopQueuePump>();
            services.AddSingleton<IWorkflowEventBus, NoopWorkflowBus>();
            services.AddTransient(provider => new BakabaseLocalizer(
                provider.GetRequiredService<IStringLocalizer<SharedResource>>()));
        });
        _service = _services.GetRequiredService<DownloadTaskService>();
        _manager = _services.GetRequiredService<DownloaderManager>();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        foreach (var downloader in _manager.Downloaders.Values)
            await downloader.StopAndWait(DownloaderStopBy.ManuallyStop);
        if (_services is IAsyncDisposable disposable) await disposable.DisposeAsync();
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
    }

    private async Task<DownloadTask> Add(string account = "account-a", ThirdPartyId source = ThirdPartyId.BaiduNetdisk,
        string? downloadPath = null)
    {
        var response = await _service.AddRange([new DownloadTask
        {
            Key = Guid.NewGuid().ToString("N"), Name = "Concurrency test", ThirdPartyId = source,
            DownloadPath = downloadPath ?? Path.Combine(_directory, Guid.NewGuid().ToString("N")), Status = DownloadTaskStatus.Idle,
            Options = account, Checkpoint = "retained checkpoint", AutoRetry = true
        }]);
        return await _service.GetDto(response.Data!.Single().Id);
    }

    [TestMethod]
    public async Task AutomaticPassFillsCapacityAndCompletionAdvancesTheQueue()
    {
        var first = await Add(); var second = await Add(); var third = await Add();
        await _service.TryStartAllTasks(DownloadTaskStartMode.AutoStart, null, DownloadTaskActionOnConflict.Ignore);

        CollectionAssert.AreEquivalent(new[] {first.Id, second.Id}, _factory.RunningIds());
        Assert.AreEqual(DownloadTaskStatus.InQueue, (await _service.GetDto(third.Id)).Status);
        Assert.AreEqual("retained checkpoint", (await _service.GetDto(third.Id)).Checkpoint);
        await _factory.Instances[first.Id].Complete();
        await _service.TryStartAllTasks(DownloadTaskStartMode.AutoStart, null, DownloadTaskActionOnConflict.Ignore);

        CollectionAssert.AreEquivalent(new[] {second.Id, third.Id}, _factory.RunningIds());
        CollectionAssert.AreEquivalent(new[] {first.Id, second.Id, third.Id},
            (await _service.GetAllDto()).Select(t => t.Id).ToArray());
        Assert.IsTrue((await _service.GetAllDto()).All(t => t.Options == "account-a"));
    }

    [TestMethod]
    public async Task ConcurrentManualAndAutomaticStartsCannotOversubscribeStartingSlots()
    {
        var first = await Add(); var second = await Add(); var third = await Add();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.BeforeStart = async task =>
        {
            if (task.Id != first.Id) return;
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };
        var firstStart = _manager.Start(first, false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondStart = _manager.Start(second, false, startAutomatically: true);
        var thirdStart = _manager.Start(third, false);
        try
        {
            Assert.IsFalse(secondStart.IsCompleted, "Capacity check must wait for the first admission to publish its status.");
            Assert.IsFalse(thirdStart.IsCompleted);
        }
        finally { release.TrySetResult(); }
        var responses = await Task.WhenAll(firstStart, secondStart, thirdStart).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(2, responses.Count(r => r.Code == (int)ResponseCode.Success));
        Assert.AreEqual(1, responses.Count(r => r.Code == (int)ResponseCode.Conflict));
        Assert.AreEqual(2, _factory.RunningIds().Length);
        Assert.AreEqual(2, _factory.Instances.Values.Sum(d => d.Starts));
    }

    [TestMethod]
    public async Task LoweringLimitsKeepsActiveTransfersAndRepeatedStartIsANoop()
    {
        var first = await Add(); var second = await Add(); var third = await Add();
        await _manager.Start(first, false); await _manager.Start(second, false);
        _policy.SourceLimit = 1; _policy.PartitionLimit = 1;

        Assert.AreEqual((int)ResponseCode.Success, (await _manager.Start(first, false)).Code);
        Assert.AreEqual(1, _factory.Instances[first.Id].Starts);
        Assert.AreEqual(2, _factory.RunningIds().Length);
        Assert.AreEqual((int)ResponseCode.Conflict, (await _manager.Start(third, false)).Code);
        await _factory.Instances[first.Id].Complete();
        Assert.AreEqual((int)ResponseCode.Conflict, (await _manager.Start(third, false)).Code);
        await _factory.Instances[second.Id].Complete();
        Assert.AreEqual((int)ResponseCode.Success, (await _manager.Start(third, false)).Code);
    }

    [TestMethod]
    public async Task FullAccountDoesNotHideAnotherAccountAndBothRespectSourceCapacity()
    {
        _policy.PartitionLimit = 1;
        var first = await Add(); var sameAccount = await Add(); var otherAccount = await Add("account-b");
        var thirdAccount = await Add("account-c");
        await _service.TryStartAllTasks(DownloadTaskStartMode.AutoStart, null, DownloadTaskActionOnConflict.Ignore);

        CollectionAssert.AreEquivalent(new[] {first.Id, otherAccount.Id}, _factory.RunningIds());
        Assert.AreEqual(DownloadTaskStatus.InQueue, (await _service.GetDto(sameAccount.Id)).Status);
        Assert.AreEqual(DownloadTaskStatus.InQueue, (await _service.GetDto(thirdAccount.Id)).Status);
        await _factory.Instances[otherAccount.Id].Complete();
        Assert.AreEqual(DownloadTaskStatus.InQueue, (await _service.GetDto(sameAccount.Id)).Status,
            "An account limit still queues this task when the source has a free slot.");
        Assert.AreEqual(DownloadTaskStatus.Idle, (await _service.GetDto(thirdAccount.Id)).Status);
    }

    [TestMethod]
    public async Task UnknownAdmittedPartitionStillCountsWhenAccountLimitsBecomeKnown()
    {
        _policy.PartitionUnknown = true;
        var first = await Add(); var second = await Add();
        await _manager.Start(first, false);

        // A restarted host learns the default account during the first task's async preflight.
        // That active transfer must still count against the newly known account's single slot.
        _policy.PartitionUnknown = false;
        _policy.PartitionLimit = 1;
        Assert.AreEqual((int)ResponseCode.Conflict, (await _manager.Start(second, false)).Code);
        Assert.AreEqual(DownloadTaskStatus.InQueue, (await _service.GetDto(second.Id)).Status);
        CollectionAssert.AreEquivalent(new[] {first.Id}, _factory.RunningIds());
        await _factory.Instances[first.Id].Complete();
        Assert.AreEqual((int)ResponseCode.Success, (await _manager.Start(second, false)).Code);
    }

    [TestMethod]
    public async Task UnconfiguredSourcesRetainOneIndependentSlot()
    {
        var first = await Add(source: ThirdPartyId.ExHentai);
        var second = await Add(source: ThirdPartyId.ExHentai);
        var independent = await Add(source: ThirdPartyId.Pixiv);
        await _service.TryStartAllTasks(DownloadTaskStartMode.AutoStart, null, DownloadTaskActionOnConflict.Ignore);

        CollectionAssert.AreEquivalent(new[] {first.Id, independent.Id}, _factory.RunningIds());
        Assert.AreEqual(DownloadTaskStatus.InQueue, (await _service.GetDto(second.Id)).Status);
    }

    [DataTestMethod]
    [DataRow("shared", "shared/.")]
    [DataRow("shared", "shared/nested")]
    [DataRow("shared/nested", "shared")]
    public async Task OverlappingOutputDirectoriesWaitWhileSiblingDirectoriesCanRun(string firstPath, string secondPath)
    {
        var first = await Add(downloadPath: Path.Combine(_directory, firstPath));
        var overlap = await Add(downloadPath: Path.Combine(_directory, secondPath));
        var sibling = await Add(downloadPath: Path.Combine(_directory, "shared-sibling"));
        await _manager.Start(first, false);

        Assert.AreEqual((int)ResponseCode.Conflict, (await _manager.Start(overlap, false)).Code);
        Assert.AreEqual(DownloadTaskStatus.InQueue, (await _service.GetDto(overlap.Id)).Status);
        Assert.AreEqual((int)ResponseCode.Success, (await _manager.Start(sibling, false)).Code);
        CollectionAssert.AreEquivalent(new[] {first.Id, sibling.Id}, _factory.RunningIds());
        await _factory.Instances[first.Id].Complete();
        Assert.AreEqual((int)ResponseCode.Success, (await _manager.Start(overlap, false)).Code);
    }

    [TestMethod]
    public async Task StopOthersDrainsOnlyRequiredSlotsAndDoesNotEvictItsOwnBatch()
    {
        var first = await Add(); var second = await Add(); var third = await Add(); var fourth = await Add();
        await _manager.Start(first, false); await _manager.Start(second, false);
        var draining = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Instances[first.Id].BeforeStop = async () =>
        {
            draining.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };
        var start = _service.TryStartAllTasks(DownloadTaskStartMode.ManualStart, [third.Id, fourth.Id],
            DownloadTaskActionOnConflict.StopOthers, skipSatisfiedTasks: false);
        await draining.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.IsFalse(start.IsCompleted);
            Assert.IsFalse(_factory.Instances.ContainsKey(third.Id), "Replacement must wait for the prior transfer to drain.");
        }
        finally { release.TrySetResult(); }
        Assert.AreEqual((int)ResponseCode.Success, (await start.WaitAsync(TimeSpan.FromSeconds(10))).Code);

        CollectionAssert.AreEquivalent(new[] {second.Id, third.Id}, _factory.RunningIds());
        Assert.AreEqual(DownloaderStopBy.AppendToTheQueue, _factory.Instances[first.Id].StoppedBy);
        Assert.AreEqual(DownloadTaskStatus.InQueue, (await _service.GetDto(fourth.Id)).Status);
    }

    private sealed class Policy : IDownloadTaskConcurrencyPolicy
    {
        public int SourceLimit = 2;
        public int PartitionLimit = 2;
        public bool PartitionUnknown;
        public ThirdPartyId ThirdPartyId => ThirdPartyId.BaiduNetdisk;
        public DownloadTaskConcurrencyLimits GetLimits(int taskId, string? options) =>
            PartitionUnknown ? new() : new(SourceLimit, options ?? "unknown", PartitionLimit);
    }

    private sealed class Factory : IDownloaderFactory
    {
        public ConcurrentDictionary<int, FakeDownloader> Instances { get; } = new();
        public Func<DownloadTask, Task> BeforeStart { get; set; } = _ => Task.CompletedTask;
        public int[] RunningIds() => Instances.Where(p => p.Value.IsOccupyingDownloadTaskSource()).Select(p => p.Key).ToArray();
        public List<DownloaderDefinition> GetDefinitions() => [];
        public IDownloaderHelper GetHelper(ThirdPartyId thirdPartyId, int taskType) => new Helper();
        public IDownloader GetDownloader(ThirdPartyId thirdPartyId, int taskType) => new FakeDownloader(this, thirdPartyId);
    }

    private sealed class Helper : IDownloaderHelper
    {
        public Task<BaseResponse> ValidateOptionsAsync() => Task.FromResult(BaseResponseBuilder.Ok);
        public Task<DownloaderOptions> GetOptionsAsync() => Task.FromResult(new DownloaderOptions());
        public Task PutOptionsAsync(DownloaderOptions options) => Task.CompletedTask;
        public Task<DownloadTask[]> BuildTasks(DownloadTaskAddInputModel model) => Task.FromResult(Array.Empty<DownloadTask>());
    }

    private sealed class FakeDownloader(Factory factory, ThirdPartyId source) : IDownloader
    {
        public int Starts;
        public Func<Task> BeforeStop = () => Task.CompletedTask;
        public ThirdPartyId ThirdPartyId => source;
        public int TaskType => 0;
        public DownloaderStatus Status { get; private set; } = DownloaderStatus.JustCreated;
        public string? Current => null;
        public double? EstimatedRemainingSeconds => null;
        public double? DownloadSpeedBytesPerSecond => null;
        public DownloaderStopBy? StoppedBy { get; set; }
        public string? Message => null;
        public int FailureTimes => 0;
        public string? Checkpoint => "retained checkpoint";
        public DateTime LastActivityAt => DateTime.Now;
        public event Func<Task>? OnStatusChanged;
        public event Func<string, Task>? OnNameAcquired;
        public event Func<decimal, Task>? OnProgress;
        public event Func<Task>? OnDownloadSpeedChanged;
        public event Func<string, long, Task>? OnFileDownloaded;
        public event Func<Task>? OnCurrentChanged;
        public event Func<string, Task>? OnCheckpointChanged;
        public async Task<bool> Start(DownloadTask task)
        {
            await factory.BeforeStart(task);
            factory.Instances[task.Id] = this;
            Starts++;
            Status = DownloaderStatus.Downloading;
            if (OnStatusChanged != null) await OnStatusChanged();
            return true;
        }
        public async Task Complete()
        {
            Status = DownloaderStatus.Complete;
            if (OnStatusChanged != null) await OnStatusChanged();
        }
        public async Task Stop(DownloaderStopBy stopBy)
        {
            await BeforeStop();
            StoppedBy = stopBy;
            Status = DownloaderStatus.Stopped;
            if (OnStatusChanged != null) await OnStatusChanged();
        }
        public Task StopAndWait(DownloaderStopBy stopBy) => Stop(stopBy);
        public void ResetStatus() => Status = DownloaderStatus.JustCreated;
        public void Dispose() { }
    }

    private sealed class NoopQueuePump(IServiceProvider services, ILogger<DownloadQueuePump> logger)
        : DownloadQueuePump(services, logger)
    {
        protected override Task RunPassAsync() => Task.CompletedTask;
    }
    private sealed class NoopWorkflowBus : IWorkflowEventBus
    {
        public Task PublishAsync<T>(string triggerKind, T payload, CancellationToken ct = default) => Task.CompletedTask;
    }
}
