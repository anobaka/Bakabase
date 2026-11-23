using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class CloudDownloadStorageReservationTests
{
    private const long Floor = 100;
    private const long Payload = 1000;
    private static string Destination(string name) => Path.Combine(Path.GetTempPath(), "cloud-storage-admission-test", name);
    private static CloudDownloadRequest Request(string id) => new(id,
        new CloudSource {Id = id, Url = "/pinned", ProviderId = "baidu", Kind = "remote"}, "account", null, Destination(id));
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestMethod, Timeout(10000)]
    public async Task KnownCloudBudgetIsAdmittedBeforeTheProviderCanTakeAnyAccountOrDownloadLease()
    {
        var guard = new StorageWriteGuard(new Policy(), _ => ("volume", Floor + StorageWriteGuard.ReservationHeadroomBytes + Payload), TimeSpan.FromMilliseconds(5));
        var release = Signal(); var started = Signal(); var waiting = Signal();
        using var stop = new CancellationTokenSource(); using var stopWaiter = new CancellationTokenSource();
        var first = new Provider([new(Destination("first"), Payload)], async ct =>
        { started.SetResult(); await release.Task.WaitAsync(ct); return new([]); });
        var second = new Provider([new(Destination("second"), Payload)], _ => Task.FromResult(new CloudDownloadResult([])));
        Task<CloudDownloadResult> Run(Provider provider, string id, Func<Task> activity, CancellationToken ct) =>
            CloudSiteDownloader<BaiduCloudTaskType>.RunWithStorageAsync(guard, provider, Request(id),
                (prepared, token) => provider.DownloadAsync(prepared, _ => Task.CompletedTask, token), activity, ct);
        var active = Run(first, "first", () => Task.CompletedTask, stop.Token);
        try
        {
            await started.Task;
            var queued = Run(second, "second", () => {waiting.TrySetResult(); return Task.CompletedTask;}, stopWaiter.Token);
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(1, second.Estimates); Assert.AreEqual(0, second.Downloads);
            stopWaiter.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => queued);
            Assert.AreEqual(0, second.Downloads, "An admission waiter must not enter the provider or hold its account lease.");
            release.SetResult(); await active;
        }
        finally {stop.Cancel(); stopWaiter.Cancel(); release.TrySetResult();}
    }

    [TestMethod]
    public async Task UnknownSizeKeepsTheExistingMonitorWithoutPretendingItsBudgetIsZero()
    {
        var guard = new StorageWriteGuard(new Policy(), _ => ("volume", Floor), TimeSpan.FromMilliseconds(5));
        var provider = new Provider(null, _ => Task.FromResult(new CloudDownloadResult([])));
        var request = Request("unknown");
        await CloudSiteDownloader<BaiduCloudTaskType>.RunWithStorageAsync(guard, provider, request,
            (prepared, ct) => provider.DownloadAsync(prepared, _ => Task.CompletedTask, ct), () => Task.CompletedTask, default);
        Assert.AreEqual(1, provider.Estimates); Assert.AreEqual(1, provider.Downloads);
    }

    [TestMethod]
    public async Task KnownBudgetAlsoKeepsTheDeclaredWorkspaceUnderStorageMonitoring()
    {
        var request = Request("output") with {WorkspaceDirectory = Destination("workspace")};
        var guard = new StorageWriteGuard(new Policy(), path => path == request.WorkspaceDirectory
            ? ("workspace-volume", Floor - 1) : ("output-volume", Floor + StorageWriteGuard.ReservationHeadroomBytes + Payload),
            TimeSpan.FromMilliseconds(5));
        var provider = new Provider([new(request.OutputDirectory, Payload)], _ => Task.FromResult(new CloudDownloadResult([])));
        var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() =>
            CloudSiteDownloader<BaiduCloudTaskType>.RunWithStorageAsync(guard, provider, request,
                (prepared, ct) => provider.DownloadAsync(prepared, _ => Task.CompletedTask, ct), () => Task.CompletedTask, default));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code); Assert.AreEqual(0, provider.Downloads);
    }

    [TestMethod]
    public async Task ThePreparedManifestReachesTheExactDownloadInvocationWithoutChangingTheQueuedRequest()
    {
        var request = Request("prepare");
        var prepared = request with {RemoteEntry = new("baidu", request.AccountId!, request.Source.Url, "pinned-manifest")};
        var guard = new StorageWriteGuard(new Policy(), _ => ("volume", Floor + StorageWriteGuard.ReservationHeadroomBytes + Payload), TimeSpan.FromMilliseconds(5));
        var provider = new Provider([new(request.OutputDirectory, Payload)], _ => Task.FromResult(new CloudDownloadResult([])), _ => prepared);
        await CloudSiteDownloader<BaiduCloudTaskType>.RunWithStorageAsync(guard, provider, request,
            (execution, ct) => provider.DownloadAsync(execution, _ => Task.CompletedTask, ct), () => Task.CompletedTask, default);
        Assert.AreSame(prepared, provider.LastRequest); Assert.IsNull(request.RemoteEntry);
    }

    [DataTestMethod]
    [DataRow("operation")]
    [DataRow("destination")]
    [DataRow("account")]
    [DataRow("remote")]
    public async Task PreparationCannotReplaceTheOperationDestinationAccountOrRemoteObject(string changed)
    {
        var request = Request("prepare");
        var prepared = changed switch
        {
            "operation" => request with {OperationId = "other"},
            "destination" => request with {OutputDirectory = Destination("other")},
            "account" => request with {AccountId = "other"},
            _ => request with {RemoteEntry = new("baidu", request.AccountId!, "/other", "manifest")}
        };
        var guard = new StorageWriteGuard(new Policy(), _ => ("volume", Floor + StorageWriteGuard.ReservationHeadroomBytes + Payload), TimeSpan.FromMilliseconds(5));
        var provider = new Provider([new(request.OutputDirectory, Payload)], _ => Task.FromResult(new CloudDownloadResult([])), _ => prepared);
        var error = await Assert.ThrowsExactlyAsync<CloudProviderException>(() =>
            CloudSiteDownloader<BaiduCloudTaskType>.RunWithStorageAsync(guard, provider, request,
                (execution, ct) => provider.DownloadAsync(execution, _ => Task.CompletedTask, ct), () => Task.CompletedTask, default));
        Assert.AreEqual("CLOUD_DOWNLOAD_PREPARATION_IDENTITY_CHANGED", error.Message); Assert.AreEqual(0, provider.Downloads);
    }

    [DataTestMethod]
    [DataRow(1000L, 0, true)]
    [DataRow(300L, 1, true)]
    [DataRow(299L, 1, false)]
    public async Task AdmissionUsesVerifiedRetainedBudgetOnlyWhenTheFullPayloadCannotFit(long available, int expectedChecks, bool succeeds)
    {
        var request = Request("retained");
        var guard = new StorageWriteGuard(new Policy(), _ => ("volume", Floor + StorageWriteGuard.ReservationHeadroomBytes + available), TimeSpan.FromMilliseconds(5));
        var provider = new Provider([new(request.OutputDirectory, Payload)], _ => Task.FromResult(new CloudDownloadResult([])));
        provider.RetainedPlan = new(request with {ReadOnlyProviderFileKeys = ["existing"]}, [new(request.OutputDirectory, 300)]);
        var activity = 0;
        var run = CloudSiteDownloader<BaiduCloudTaskType>.RunWithStorageAsync(guard, provider, request,
            (prepared, token) => provider.DownloadAsync(prepared, _ => Task.CompletedTask, token), () => Task.CompletedTask,
            default, () => {activity++; return Task.CompletedTask;});
        if (succeeds) await run;
        else Assert.AreEqual(StorageWriteGuard.ReserveLowCode, (await Assert.ThrowsExactlyAsync<LocalStorageException>(() => run)).Code);
        Assert.AreEqual(expectedChecks, activity);
        Assert.AreEqual(succeeds ? 1 : 0, provider.Downloads);
        if (succeeds) Assert.AreSame(expectedChecks == 0 ? request : provider.RetainedPlan.Request, provider.LastRequest);
    }

    [TestMethod]
    public async Task AStorageFailureAfterWriterStartsNeverLaunchesASecondAttempt()
    {
        var request = Request("started");
        var guard = new StorageWriteGuard(new Policy(), _ => ("volume", Floor + StorageWriteGuard.ReservationHeadroomBytes + Payload), TimeSpan.FromMilliseconds(5));
        var provider = new Provider([new(request.OutputDirectory, Payload)], _ => throw new LocalStorageException(StorageWriteGuard.ReserveLowCode));
        provider.RetainedPlan = new(request, [new(request.OutputDirectory, 0)]);
        await Assert.ThrowsExactlyAsync<LocalStorageException>(() => CloudSiteDownloader<BaiduCloudTaskType>.RunWithStorageAsync(
            guard, provider, request, (prepared, token) => provider.DownloadAsync(prepared, _ => Task.CompletedTask, token),
            () => Task.CompletedTask, default, () => throw new AssertFailedException("Started writer triggered a retained preflight.")));
        Assert.AreEqual(1, provider.Downloads);
    }

    private sealed class Policy : IStorageWritePolicyProvider
    { public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) => Task.FromResult(Floor); }

    private sealed class Provider(IReadOnlyList<CloudDownloadWriteBudget>? budget,
        Func<CancellationToken, Task<CloudDownloadResult>> download,
        Func<CloudDownloadRequest, CloudDownloadRequest>? prepare = null) : ICloudDownloadProvider, ICloudDownloadWriteBudgetProvider
    {
        public string ProviderId => "baidu";
        public int Estimates, Downloads;
        public CloudDownloadRequest? LastRequest;
        public CloudDownloadWritePlan? RetainedPlan;
        public async Task<CloudDownloadWritePlan?> TryPrepareRetainedWriteAsync(CloudDownloadRequest request, Func<Task> activity, CancellationToken ct)
        {
            if (RetainedPlan != null) await activity();
            return RetainedPlan;
        }
        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct) => Task.FromResult(new CloudReadiness(true));
        public Task<CloudDownloadWritePlan?> PrepareWriteAsync(CloudDownloadRequest request, CancellationToken ct)
        { Estimates++; return Task.FromResult(budget == null ? null : new CloudDownloadWritePlan(prepare?.Invoke(request) ?? request, budget)); }
        public Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
        { Downloads++; LastRequest = request; return download(ct); }
    }
}
