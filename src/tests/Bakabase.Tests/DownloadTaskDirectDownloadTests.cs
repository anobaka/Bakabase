using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
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

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadTaskDirectDownloadTests
{
    private DownloadTaskService _service = null!;
    private DownloaderManager _manager = null!;
    private FakeFactory _factory = null!;
    private FakePrecheck _precheck = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new FakeFactory();
        _precheck = new FakePrecheck();
        var sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            // Load the real messages so conflict details are verified, not resource keys.
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.AddSingleton<IDownloaderFactory>(_factory);
            services.RemoveAll<IDownloadTaskPrecheck>();
            services.AddSingleton<IDownloadTaskPrecheck>(_precheck);
            services.AddSingleton<IWorkflowEventBus>(new NoopWorkflowBus());
            services.AddTransient(provider => new BakabaseLocalizer(
                provider.GetRequiredService<IStringLocalizer<SharedResource>>()));
        });
        _service = sp.GetRequiredService<DownloadTaskService>();
        _manager = sp.GetRequiredService<DownloaderManager>();
    }

    private async Task<int> AddTask(ExHentaiDownloadTaskType type = ExHentaiDownloadTaskType.SingleWork,
        ThirdPartyId thirdParty = ThirdPartyId.ExHentai, DownloadTaskStatus status = DownloadTaskStatus.Idle)
    {
        var response = await _service.AddRange([new DownloadTask
        {
            ThirdPartyId = thirdParty, Type = (int) type,
            Status = status,
            Key = "https://exhentai.org/g/12345/abcdef0123/", Name = "My work",
            DownloadPath = "/downloads/my-work", Checkpoint = "12345-", Interval = 4321,
            StartPage = 2, EndPage = 8, AutoRetry = true,
            Options = "{\"PreferTorrent\":true,\"downloadResultWorkflowId\":19,\"torrentDownloadedAt\":\"2026-09-20T00:00:00Z\",\"futureOption\":{\"keep\":7}}"
        }]);
        return response.Data!.Single().Id;
    }

    [DataTestMethod]
    [DataRow(ExHentaiDownloadTaskType.SingleWork)]
    [DataRow(ExHentaiDownloadTaskType.List)]
    [DataRow(ExHentaiDownloadTaskType.Watched)]
    public async Task DirectDownload_OnlyChangesTorrentPreferenceAndNecessaryListCheckpoint(ExHentaiDownloadTaskType type)
    {
        var id = await AddTask(type, status: DownloadTaskStatus.Complete);
        var before = await _service.GetDto(id);
        var response = await _service.DirectDownload(id);
        Assert.AreEqual((int) ResponseCode.Success, response.Code);
        var after = await _service.GetDto(id);
        Assert.AreEqual(before.Key, after.Key);
        Assert.AreEqual(before.Name, after.Name);
        Assert.AreEqual(before.DownloadPath, after.DownloadPath);
        Assert.AreEqual(before.Interval, after.Interval);
        Assert.AreEqual(before.StartPage, after.StartPage);
        Assert.AreEqual(before.EndPage, after.EndPage);
        Assert.AreEqual(before.AutoRetry, after.AutoRetry);
        Assert.AreEqual(type == ExHentaiDownloadTaskType.SingleWork ? before.Checkpoint : null, after.Checkpoint);
        using var options = JsonDocument.Parse(after.Options!);
        Assert.IsFalse(options.RootElement.GetProperty("preferTorrent").GetBoolean());
        Assert.IsFalse(options.RootElement.TryGetProperty("PreferTorrent", out _));
        Assert.AreEqual(19, options.RootElement.GetProperty("downloadResultWorkflowId").GetInt32());
        Assert.AreEqual(7, options.RootElement.GetProperty("futureOption").GetProperty("keep").GetInt32());
        Assert.AreEqual("2026-09-20T00:00:00Z", options.RootElement.GetProperty("torrentDownloadedAt").GetString());
        Assert.AreEqual(1, _factory.Downloader.StartedOptions.Count);
        Assert.IsFalse(JsonSerializer.Deserialize<ExHentaiTaskOptions>(_factory.Downloader.StartedOptions[0]!,
            JsonSerializerOptions.Web)!.PreferTorrent);
    }

    [TestMethod]
    public async Task DirectDownload_ActiveTaskDrainsBeforeMutatingAndRestarting()
    {
        var id = await AddTask();
        await _manager.Start(await _service.GetDto(id), false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Downloader.Drain = release.Task;
        var action = _service.DirectDownload(id);
        await _factory.Downloader.Draining.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(action.IsCompleted);
        Assert.AreEqual(1, _factory.Downloader.StartedOptions.Count);
        Assert.IsTrue((await _service.GetDto(id)).GetTypedOptions<ExHentaiTaskOptions>().PreferTorrent);
        await _service.OnCheckpointReached(id, "1-3");
        release.SetResult();
        Assert.AreEqual((int) ResponseCode.Success, (await action.WaitAsync(TimeSpan.FromSeconds(5))).Code);
        Assert.AreEqual(2, _factory.Downloader.StartedOptions.Count);
        Assert.IsFalse((await _service.GetDto(id)).GetTypedOptions<ExHentaiTaskOptions>().PreferTorrent);
        Assert.AreEqual("1-3", (await _service.GetDto(id)).Checkpoint);
    }

    [TestMethod]
    public async Task AQueuedOldStartCannotRestoreTorrentPreferenceAfterDirectDownload()
    {
        var id = await AddTask();
        var oldSnapshot = await _service.GetDto(id);
        var validationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseValidation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.Helper.Validate = async () =>
        {
            validationEntered.TrySetResult();
            await releaseValidation.Task;
            return BaseResponseBuilder.Ok;
        };
        var oldStart = _manager.Start(oldSnapshot, false);
        await validationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var direct = _service.DirectDownload(id);
        var anotherStaleStart = _manager.Start(oldSnapshot, false);
        releaseValidation.SetResult();
        await oldStart.WaitAsync(TimeSpan.FromSeconds(5));
        await direct.WaitAsync(TimeSpan.FromSeconds(5));
        await anotherStaleStart.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, _factory.Downloader.StartedOptions.Count);
        Assert.IsFalse(JsonSerializer.Deserialize<ExHentaiTaskOptions>(_factory.Downloader.StartedOptions[^1]!,
            JsonSerializerOptions.Web)!.PreferTorrent);
        // Even after the direct run finishes, a pre-action DTO must read the current options.
        await _factory.Downloader.Stop(DownloaderStopBy.ManuallyStop);
        await _manager.Start(oldSnapshot, false);
        Assert.IsFalse(JsonSerializer.Deserialize<ExHentaiTaskOptions>(_factory.Downloader.StartedOptions[^1]!,
            JsonSerializerOptions.Web)!.PreferTorrent);
    }

    [TestMethod]
    public async Task DirectDownload_MissingOrOtherProviderTaskDoesNotStartAnything()
    {
        Assert.AreEqual((int) ResponseCode.NotFound, (await _service.DirectDownload(999999)).Code);
        var id = await AddTask(thirdParty: ThirdPartyId.Bilibili);
        var before = (await _service.GetDto(id)).Options;
        Assert.AreNotEqual((int) ResponseCode.Success, (await _service.DirectDownload(id)).Code);
        Assert.AreEqual(before, (await _service.GetDto(id)).Options);
        Assert.AreEqual(0, _factory.Downloader.StartedOptions.Count);
    }

    [TestMethod]
    public async Task AnOldSatisfiedPrecheckCannotOverwriteTheDirectDownloadOptionsOrCompleteIt()
    {
        var id = await AddTask();
        _precheck.BlockOnce = true;
        var oldPass = _service.TryStartAllTasks(DownloadTaskStartMode.AutoStart, [id],
            DownloadTaskActionOnConflict.Ignore);
        await _precheck.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _service.DirectDownload(id);
        _precheck.Release.SetResult();
        await oldPass.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse((await _service.GetDto(id)).GetTypedOptions<ExHentaiTaskOptions>().PreferTorrent);
        Assert.AreEqual(DownloaderStatus.Downloading, _manager[id]!.Status);
        Assert.AreEqual(1, _factory.Downloader.StartedOptions.Count);
    }

    [TestMethod]
    public async Task DirectDownload_ConfigurationFailureRemainsVisibleAfterStoppingAnActiveRun()
    {
        var id = await AddTask();
        await _manager.Start(await _service.GetDto(id), false);
        _factory.Helper.Validate = () => Task.FromResult(BaseResponseBuilder.BuildBadRequest("Invalid cookie"));
        var response = await _service.DirectDownload(id);
        Assert.AreNotEqual((int) ResponseCode.Success, response.Code);
        Assert.AreEqual(DownloadTaskStatus.Failed, (await _service.GetDto(id)).Status);
        Assert.IsFalse((await _service.GetDto(id)).GetTypedOptions<ExHentaiTaskOptions>().PreferTorrent);
        StringAssert.Contains((await _service.GetDto(id)).Message!, "Invalid cookie");
        Assert.AreEqual(1, _factory.Downloader.StartedOptions.Count);
    }

    [TestMethod]
    public async Task DirectDownload_UsesTheNormalConflictPolicy()
    {
        var occupied = await AddTask();
        var target = await AddTask();
        await _manager.Start(await _service.GetDto(occupied), false);
        var response = await _service.DirectDownload(target);
        Assert.AreEqual((int) ResponseCode.Conflict, response.Code);
        StringAssert.Contains(response.Message!, "My work");
        Assert.AreNotEqual("FailedToStart", response.Message);
        Assert.IsFalse((await _service.GetDto(target)).GetTypedOptions<ExHentaiTaskOptions>().PreferTorrent);
        Assert.AreEqual(DownloaderStatus.Downloading, _manager[occupied]!.Status);
        Assert.AreEqual((int) ResponseCode.Success,
            (await _service.DirectDownload(target, DownloadTaskActionOnConflict.StopOthers)).Code);
        Assert.AreEqual(DownloaderStatus.Stopped, _manager[occupied]!.Status);
        Assert.AreEqual(DownloaderStatus.Downloading, _manager[target]!.Status);
    }

    private sealed class FakePrecheck : IDownloadTaskPrecheck
    {
        public bool BlockOnce;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;
        public async Task<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>> EvaluateAsync(
            IReadOnlyList<DownloadTask> candidates, CancellationToken ct)
        {
            var block = BlockOnce;
            BlockOnce = false;
            var result = candidates.ToDictionary(t => t.Id, t => new DownloadTaskPrecheckVerdict(
                block && t.GetTypedOptions<ExHentaiTaskOptions>().PreferTorrent
                    ? DownloadTaskPrecheckOutcome.AlreadySatisfied : DownloadTaskPrecheckOutcome.Run));
            if (block)
            {
                Entered.SetResult();
                await Release.Task.WaitAsync(ct);
            }
            return result;
        }
    }

    private sealed class FakeFactory : IDownloaderFactory
    {
        public readonly FakeHelper Helper = new();
        public readonly FakeDownloader Downloader = new();
        private bool _created;
        public List<DownloaderDefinition> GetDefinitions() => [];
        public IDownloaderHelper GetHelper(ThirdPartyId thirdPartyId, int taskType) => Helper;
        public IDownloader GetDownloader(ThirdPartyId thirdPartyId, int taskType)
        {
            if (_created) return new FakeDownloader();
            _created = true;
            return Downloader;
        }
    }

    private sealed class FakeHelper : IDownloaderHelper
    {
        public Func<Task<BaseResponse>> Validate = () => Task.FromResult(BaseResponseBuilder.Ok);
        public Task<BaseResponse> ValidateOptionsAsync() => Validate();
        public Task<DownloaderOptions> GetOptionsAsync() => Task.FromResult(new DownloaderOptions());
        public Task PutOptionsAsync(DownloaderOptions options) => Task.CompletedTask;
        public Task<DownloadTask[]> BuildTasks(DownloadTaskAddInputModel model) => Task.FromResult(Array.Empty<DownloadTask>());
    }

    private sealed class FakeDownloader : IDownloader
    {
        public readonly List<string?> StartedOptions = [];
        public Task Drain = Task.CompletedTask;
        public readonly TaskCompletionSource Draining = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;
        public int TaskType => 1;
        public DownloaderStatus Status { get; private set; } = DownloaderStatus.JustCreated;
        public string? Current => null;
        public double? EstimatedRemainingSeconds => null;
        public double? DownloadSpeedBytesPerSecond => null;
        public DownloaderStopBy? StoppedBy { get; set; }
        public string? Message => null;
        public int FailureTimes => 0;
        public string? Checkpoint => null;
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
            StartedOptions.Add(task.Options);
            Status = DownloaderStatus.Downloading;
            if (OnStatusChanged != null) await OnStatusChanged();
            return true;
        }
        public async Task Stop(DownloaderStopBy stopBy)
        {
            StoppedBy = stopBy;
            Status = DownloaderStatus.Stopped;
            if (OnStatusChanged != null) await OnStatusChanged();
        }
        public async Task StopAndWait(DownloaderStopBy stopBy)
        {
            Draining.TrySetResult();
            await Drain;
            await Stop(stopBy);
        }
        public void ResetStatus() => Status = DownloaderStatus.JustCreated;
        public void Dispose() { }
    }

    private sealed class NoopWorkflowBus : IWorkflowEventBus
    {
        public Task PublishAsync<T>(string triggerKind, T payload, CancellationToken ct = default) => Task.CompletedTask;
    }
}
