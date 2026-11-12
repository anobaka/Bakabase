using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.Baidu;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class BaiduDownloadConcurrencyTests
{
    [TestMethod]
    public async Task ReadersShareProfileButQueuedMutationBlocksLaterReaders()
    {
        var gate = new BaiduAccountLease();
        var first = await gate.AcquireAsync(false, default);
        var second = await gate.AcquireAsync(false, default);
        var writer = gate.AcquireAsync(true, default);
        var laterReader = gate.AcquireAsync(false, default);
        Assert.IsFalse(writer.IsCompleted);
        Assert.IsFalse(laterReader.IsCompleted);
        first.Dispose();
        Assert.IsFalse(writer.IsCompleted);
        second.Dispose();
        using var mutation = await writer.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(laterReader.IsCompleted);
        mutation.Dispose();
        using var finalReader = await laterReader.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task CancelledWriterReleasesQueueWithoutLeakingLease()
    {
        var gate = new BaiduAccountLease();
        using var first = await gate.AcquireAsync(false, default);
        using var cancellation = new CancellationTokenSource();
        var writer = gate.AcquireAsync(true, cancellation.Token);
        var next = gate.AcquireAsync(false, default);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await writer);
        using var reader = await next.WaitAsync(TimeSpan.FromSeconds(2));
        first.Dispose();
        reader.Dispose();
        using var exclusive = await gate.AcquireAsync(true, default).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task ProviderSlotsBoundDirectCallsAndLoweringDoesNotCancelRunningWork()
    {
        var admission = new BaiduDownloadAdmission();
        var limit = 2;
        var first = await admission.AcquireAsync(() => limit, default);
        var second = await admission.AcquireAsync(() => limit, default);
        var third = admission.AcquireAsync(() => limit, default);
        Assert.IsFalse(third.IsCompleted);
        limit = 1;
        first.Dispose();
        Assert.IsFalse(third.IsCompleted);
        second.Dispose();
        using var replacement = await third.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancel = new CancellationTokenSource();
        var cancelled = admission.AcquireAsync(() => limit, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await cancelled);
        limit = 2;
        admission.NotifyChanged();
        using var another = await admission.AcquireAsync(() => limit, default).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task FiveProviderDownloadsCanRunAndSixthWaitsUntilOneReleases()
    {
        var admission = new BaiduDownloadAdmission();
        var running = new List<IDisposable>();
        try
        {
            for (var index = 0; index < 5; index++)
                running.Add(await admission.AcquireAsync(() => 5, default).WaitAsync(TimeSpan.FromSeconds(2)));
            var sixth = admission.AcquireAsync(() => 5, default);
            Assert.IsFalse(sixth.IsCompleted);
            running[0].Dispose();
            running.RemoveAt(0);
            running.Add(await sixth.WaitAsync(TimeSpan.FromSeconds(2)));
            // A malformed persisted limit must not allow a sixth download either.
            using var cancellation = new CancellationTokenSource();
            var beyondLimit = admission.AcquireAsync(() => 500, cancellation.Token);
            Assert.IsFalse(beyondLimit.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await beyondLimit);
        }
        finally { foreach (var lease in running) lease.Dispose(); }
        using var afterCompletion = await admission.AcquireAsync(() => 1, default).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task OverlappingOutputsWaitWhileIndependentRootsRunTogether()
    {
        var gate = new BaiduDownloadDestinationLease();
        var root = Path.GetFullPath("payload-fixture");
        using var first = await gate.AcquireAsync(Path.Combine(root, "task-one"), default);
        using var independent = await gate.AcquireAsync(Path.Combine(root, "task-two"), default);
        var nested = gate.AcquireAsync(Path.Combine(root, "task-one", "nested"), default);
        Assert.IsFalse(nested.IsCompleted);
        using var cancellation = new CancellationTokenSource();
        var parent = gate.AcquireAsync(root, cancellation.Token);
        Assert.IsFalse(parent.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await parent);
        first.Dispose();
        using var resumed = await nested.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void MembershipIsEvidenceBasedAndFreshNonSvipOverridesManualConfirmation()
    {
        var now = DateTimeOffset.UtcNow;
        var account = Account() with { DownloadOptions = new() { MaxConcurrentTasks = 5, ConnectionsPerFile = 10 } };
        Assert.AreEqual(1, BaiduDownloadPolicy.Evaluate(account, now).MaxConcurrentTasks);
        account = account with { Membership = new() { Kind = "svip", CheckedAt = now } };
        Assert.AreEqual(5, BaiduDownloadPolicy.Evaluate(account, now).MaxConcurrentTasks);
        Assert.AreEqual(10, BaiduDownloadPolicy.Evaluate(account, now).ConnectionsPerFile);
        Assert.AreEqual(1, BaiduDownloadPolicy.Evaluate(account, now.AddHours(6)).MaxConcurrentTasks);
        account = account with { Membership = account.Membership with { FailureCode = "BAIDU_MEMBERSHIP_TIMEOUT" } };
        Assert.AreEqual(1, BaiduDownloadPolicy.Evaluate(account, now).MaxConcurrentTasks);
        account = account with { DownloadOptions = account.DownloadOptions with { UserConfirmedSvip = true } };
        Assert.AreEqual(5, BaiduDownloadPolicy.Evaluate(account, now).MaxConcurrentTasks);
        foreach (var kind in new[] { "free", "vip" })
        {
            var downgraded = account with { Membership = new() { Kind = kind, CheckedAt = now } };
            Assert.AreEqual(1, BaiduDownloadPolicy.Evaluate(downgraded, now).MaxConcurrentTasks);
            Assert.AreEqual(1, BaiduDownloadPolicy.Evaluate(downgraded, now).ConnectionsPerFile);
        }
        Assert.AreEqual(1, BaiduDownloadPolicy.Evaluate(account with { IsLoggedIn = false }, now).MaxConcurrentTasks);
    }

    [DataTestMethod]
    [DataRow(1, 1, true)]
    [DataRow(5, 10, true)]
    [DataRow(5, 20, true)]
    [DataRow(6, 10, false)]
    [DataRow(0, 10, false)]
    [DataRow(5, 21, false)]
    public void DownloadOptionsExposeConsistentSupportedBounds(int tasks, int connections, bool valid)
    {
        var options = new BaiduDownloadOptions { MaxConcurrentTasks = tasks, ConnectionsPerFile = connections };
        Assert.AreEqual(valid, Validator.TryValidateObject(options, new ValidationContext(options), [], true));
    }

    [TestMethod]
    public void SourceCapacityPreservesAccountPartitionsAndCachedIdentityAfterDefaultChanges()
    {
        var a = Account() with
        {
            Id = Guid.NewGuid().ToString("N"),
            DownloadOptions = new() { MaxConcurrentTasks = 5, ConnectionsPerFile = 10 },
            Membership = new() { Kind = "svip", CheckedAt = DateTimeOffset.UtcNow }
        };
        var b = a with { Id = Guid.NewGuid().ToString("N"), IsDefault = false, Membership = new() { Kind = "vip", CheckedAt = DateTimeOffset.UtcNow } };
        var snapshot = new BaiduAccountSnapshot();
        snapshot.Update([a, b]);
        var policy = new BaiduDownloadTaskConcurrencyPolicy(snapshot);
        var remote = Request("[]");
        string Options(BaiduPcsAccountViewModel account) => JsonSerializer.Serialize(remote with
        { AccountId = account.Id, RemoteEntry = remote.RemoteEntry! with { AccountId = account.Id } }, JsonSerializerOptions.Web);
        var svip = policy.GetLimits(1, Options(a));
        var ordinaryVip = policy.GetLimits(2, Options(b));
        Assert.AreEqual(5, svip.SourceLimit);
        Assert.AreEqual(5, svip.PartitionLimit);
        Assert.AreEqual(1, ordinaryVip.PartitionLimit);
        Assert.AreNotEqual(svip.PartitionKey, ordinaryVip.PartitionKey);

        var defaultAccountRequest = JsonSerializer.Serialize(remote with { AccountId = null, RemoteEntry = null }, JsonSerializerOptions.Web);
        var pinnedDefault = policy.GetLimits(3, defaultAccountRequest);
        Assert.AreEqual(svip.PartitionKey, pinnedDefault.PartitionKey);
        snapshot.Update([a with { IsDefault = false, DownloadOptions = a.DownloadOptions with { MaxConcurrentTasks = 2 } }, b with { IsDefault = true }]);
        var retained = policy.GetLimits(3, null);
        Assert.AreEqual(svip.PartitionKey, retained.PartitionKey);
        Assert.AreEqual(2, retained.PartitionLimit);
        Assert.AreEqual(ordinaryVip.PartitionKey, policy.GetLimits(4, defaultAccountRequest).PartitionKey);
        Assert.AreEqual(1, policy.GetLimits(5, null).SourceLimit);
        Assert.AreEqual(1, policy.GetLimits(5, null).PartitionLimit);

        snapshot.Update([a with { Membership = a.Membership with { Kind = "free" } }, b]);
        Assert.AreEqual(1, policy.GetLimits(1, null).PartitionLimit);
    }

    [TestMethod]
    public void OldMetadataDefaultsToSingleTaskAndConnection()
    {
        var old = JsonSerializer.Deserialize<BaiduPcsAccountViewModel>(
            """{"id":"account","name":"test","uid":"123","isDefault":true,"isLoggedIn":true}""", JsonSerializerOptions.Web)!;
        Assert.AreEqual(1, old.EffectiveMaxConcurrentTasks);
        Assert.AreEqual(1, old.EffectiveConnectionsPerFile);
        Assert.AreEqual("unknown", old.Membership.Kind);
    }

    [TestMethod]
    public void ReloginPreservesOptionsAndDefaultSelectionCommittedDuringAuthentication()
    {
        var beforeLogin = Account();
        var authenticated = beforeLogin with
        {
            Name = "new name", LastValidatedAt = DateTimeOffset.UtcNow,
            Membership = new() { Kind = "svip", CheckedAt = DateTimeOffset.UtcNow }
        };
        var savedWhileAuthenticating = beforeLogin with
        {
            IsDefault = false,
            DownloadOptions = new() { MaxConcurrentTasks = 2, ConnectionsPerFile = 4, UserConfirmedSvip = true }
        };
        var merged = BaiduPcsAccountStore.MergeLoginMetadata(authenticated, savedWhileAuthenticating);
        Assert.AreEqual(savedWhileAuthenticating.DownloadOptions, merged.DownloadOptions);
        Assert.IsFalse(merged.IsDefault);
        Assert.AreEqual(authenticated.Membership, merged.Membership);
        Assert.AreEqual(authenticated.LastValidatedAt, merged.LastValidatedAt);
        Assert.AreEqual("new name", merged.Name);
        Assert.AreEqual(authenticated, BaiduPcsAccountStore.MergeLoginMetadata(authenticated, null));
    }

    [TestMethod]
    public void PinnedWriteBudgetReservesAllFilesAndRejectsIncompleteEvidence()
    {
        var request = Request("""[{"id":1,"path":"/remote","size":999,"isDirectory":true},{"id":2,"path":"/remote/a","size":100,"isDirectory":false},{"id":3,"path":"/remote/b","size":200,"isDirectory":false}]""");
        var budget = BaiduCloudDownloadProvider.EstimateWriteBudget(request)!;
        Assert.AreEqual(300L, budget.Single().MaximumAdditionalBytes);
        Assert.AreEqual(request.OutputDirectory, budget.Single().Destination);
        Assert.IsNull(BaiduCloudDownloadProvider.EstimateWriteBudget(request with { RemoteEntry = request.RemoteEntry! with { ManifestJson = null } }));
        foreach (var json in new[]
                 {
                     "[]", "{}", "[{\"id\":1}]",
                     """[{"id":1,"path":"/a","size":-1,"isDirectory":false}]""",
                     """[{"id":1,"path":"/a","size":1,"isDirectory":false},{"id":1,"path":"/b","size":1,"isDirectory":false}]""",
                     """[{"id":1,"path":"/a","size":9223372036854775807,"isDirectory":false},{"id":2,"path":"/b","size":1,"isDirectory":false}]"""
                 })
            Assert.ThrowsExactly<CloudProviderException>(() => BaiduCloudDownloadProvider.EstimateWriteBudget(Request(json)));
    }

    [TestMethod]
    public async Task PinnedLegacyRequestKeepsItsRemoteAccountWhenOuterAccountIsAbsent()
    {
        var original = Request("""[{"id":1,"path":"/remote/a","size":100,"isDirectory":false}]""") with { AccountId = null };
        // An existing pinned manifest needs no account lookup or bridge request.
        var provider = new BaiduCloudDownloadProvider(null!, null!, null!);
        var plan = await provider.PrepareWriteAsync(original, default);
        Assert.IsNotNull(plan);
        Assert.AreEqual(original.RemoteEntry!.AccountId, plan.Request.AccountId);
        Assert.AreEqual(original.RemoteEntry, plan.Request.RemoteEntry);
        Assert.AreEqual(original.OutputDirectory, plan.Request.OutputDirectory);
        Assert.IsNull(original.AccountId);
        Assert.AreEqual(100L, plan.Budgets.Single().MaximumAdditionalBytes);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(async () =>
            await provider.PrepareWriteAsync(original with { AccountId = "different-account" }, default));
    }

    private static BaiduPcsAccountViewModel Account() => new("fixture", "test", "123", true, true, null);
    private static CloudDownloadRequest Request(string manifest) => new("operation", new() { Id = "source", Url = "/remote", ProviderId = "baidu" },
        "account", new("baidu", "account", "/remote", manifest), Path.GetFullPath("fixture-output"));
}
