using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadStorageRetryTests
{
    [DataTestMethod]
    [DataRow("LOCAL_STORAGE_FULL")]
    [DataRow("LOCAL_STORAGE_WRITE_FAILED")]
    [DataRow("LOCAL_STORAGE_ACCESS_DENIED")]
    [DataRow("LOCAL_STORAGE_UNAVAILABLE")]
    public void ClearingVolumeHoldDoesNotAuthorizeAutomaticRetryOfAnotherStorageFailure(string code)
    {
        var admission = new DownloadStorageAdmission(_ => "/nas", _ => 1024L * 1024 * 1024, _ => { });
        using var services = new ServiceCollection().AddSingleton(admission).BuildServiceProvider();
        var manager = Manager(services);
        var typedMessage = "Download failed.\n" + new AggregateException(new LocalStorageException(code));
        var task = Row(typedMessage);
        var downloader = new RetainedDownloader {Message = typedMessage};
        Attach(manager, task.Id, downloader);

        admission.ObserveFailure(task.DownloadPath, new LocalStorageException("LOCAL_STORAGE_FULL"));
        admission.BeforeStart("/nas/other-queued-task", explicitContinuation: true);
        Assert.IsNull(admission.BlockReason(task.DownloadPath), "The sibling's explicit continuation cleared the volume hold.");
        var dto = task.ToDomainModel(manager)!;
        Assert.AreEqual(DownloadTaskStatus.Failed, dto.Status);
        Assert.IsTrue(dto.AutoRetry, "Keep the user's preference; block only this unresolved failure.");
        Assert.IsNull(dto.NextStartDt);
        Assert.IsFalse(dto.AvailableActions.Contains(DownloadTaskAction.StartAutomatically));
        Assert.IsTrue(dto.AvailableActions.Contains(DownloadTaskAction.Restart));
        Assert.AreEqual("keep-existing-range", dto.Checkpoint);

        // Persistence, not a retained service-local hold, keeps the restriction after restart.
        var restarted = task.ToDomainModel(Manager(services))!;
        Assert.AreEqual(DownloadTaskStatus.Failed, restarted.Status);
        Assert.IsNull(restarted.NextStartDt);
        Assert.IsFalse(restarted.AvailableActions.Contains(DownloadTaskAction.StartAutomatically));

        // An explicitly started task's later, unrelated failure follows normal AutoRetry policy.
        downloader.Message = task.Message = "Network connection reset";
        dto = task.ToDomainModel(manager)!;
        Assert.IsNotNull(dto.NextStartDt);
        Assert.IsTrue(dto.AvailableActions.Contains(DownloadTaskAction.StartAutomatically));
    }

    [DataTestMethod]
    [DataRow("The provider mentioned LOCAL_STORAGE_FULL while explaining its response.")]
    [DataRow("CloudProviderException: LOCAL_STORAGE_FULL")]
    [DataRow("https://example.invalid/LOCAL_STORAGE_FULL")]
    [DataRow("LocalStorageException: LOCAL_STORAGE_FULL")]
    [DataRow("BAIDU_OUTPUT_DIRECTORY_FAILED")]
    public void ArbitraryTextDoesNotBecomeTypedLocalStorageEvidence(string message)
    {
        Assert.IsNull(LocalStorageFailures.PersistedCode(message));
        using var services = new ServiceCollection().BuildServiceProvider();
        var manager = Manager(services);
        var task = Row(message);
        Attach(manager, task.Id, new RetainedDownloader {Message = message});
        Assert.IsTrue(task.ToDomainModel(manager)!.AvailableActions.Contains(DownloadTaskAction.StartAutomatically));
    }

    [TestMethod]
    public void StartRejectionsAndNestedTypedExceptionDeclarationsAreRecognizedPrecisely()
    {
        Assert.AreEqual("LOCAL_STORAGE_ACCESS_DENIED", LocalStorageFailures.PersistedCode("LOCAL_STORAGE_ACCESS_DENIED"));
        var nested = new AggregateException(new InvalidOperationException("Unrelated"), new LocalStorageException("LOCAL_STORAGE_UNAVAILABLE"));
        Assert.AreEqual("LOCAL_STORAGE_UNAVAILABLE", LocalStorageFailures.PersistedCode(nested.ToString()));
        Assert.IsNull(LocalStorageFailures.PersistedCode(typeof(LocalStorageException).FullName + ": LOCAL_STORAGE_FULL is mentioned in arbitrary prose"));
    }

    private static DownloadTaskDbModel Row(string message) => new()
    {
        Id = 701, ThirdPartyId = ThirdPartyId.ExHentai, Type = 1, DownloadPath = "/nas/retained",
        Status = DownloadTaskDbModelStatus.Failed, AutoRetry = true, Message = message,
        DownloadStatusUpdateDt = DateTime.Now.AddDays(-1), Checkpoint = "keep-existing-range"
    };

    private static DownloaderManager Manager(IServiceProvider services) =>
        new(services, null!, NullLogger<DownloaderManager>.Instance, null!, null!);

    private static void Attach(DownloaderManager manager, int id, IDownloader downloader) =>
        ((ConcurrentDictionary<int, IDownloader>)typeof(DownloaderManager)
            .GetField("_downloaders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!)
        [id] = downloader;

    // A retained downloader state is input to the real DTO projection; no file or network work runs.
    private sealed class RetainedDownloader : IDownloader
    {
        public ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;
        public int TaskType => 1;
        public DownloaderStatus Status => DownloaderStatus.Failed;
        public string? Current => null;
        public double? EstimatedRemainingSeconds => null;
        public double? DownloadSpeedBytesPerSecond => null;
        public DownloaderStopBy? StoppedBy { get; set; }
        public string? Message { get; set; }
        public int FailureTimes => 1;
        public string? Checkpoint => "keep-existing-range";
        public DateTime LastActivityAt => DateTime.Now;
        public Task Stop(DownloaderStopBy stopBy) => Task.CompletedTask;
        public Task<bool> Start(DownloadTask task) => throw new NotSupportedException();
        public void ResetStatus() { }
        public void Dispose() { }
#pragma warning disable CS0067
        public event Func<Task>? OnStatusChanged;
        public event Func<string, Task>? OnNameAcquired;
        public event Func<decimal, Task>? OnProgress;
        public event Func<Task>? OnDownloadSpeedChanged;
        public event Func<string, long, Task>? OnFileDownloaded;
        public event Func<Task>? OnCurrentChanged;
        public event Func<string, Task>? OnCheckpointChanged;
#pragma warning restore CS0067
    }
}
