using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class PikPakCliTests
{
    private Task<CloudDownloadResult> QueueOriginal(Func<Task> waiting, CancellationToken ct = default) =>
        PikPakCloudDownloader.RunWithCloudCapacityAsync(_provider, Request,
            () => _provider.DownloadAsync(Request, _ => Task.CompletedTask, ct),
            waiting, ct, TimeSpan.Zero);

    [DataTestMethod]
    [DataRow(1, false, true, null)]
    [DataRow(1, true, true, null)]
    [DataRow(2, false, true, "PIKPAK_CLI_RESPONSE_INVALID")]
    [DataRow(0, false, false, "PIKPAK_ACCOUNT_REQUIRED")]
    public async Task AccountReadinessRetriesAMalformedStatusOnce(int malformedReplies, bool omitLoginStatus,
        bool loggedIn, string? problem)
    {
        await Login();
        _native.Calls.Clear();
        _native.Approved = loggedIn;
        _native.MalformedStatusReplies = malformedReplies;
        _native.OmitLoginStatus = omitLoginStatus;

        var readiness = await _provider.CheckReadyAsync(null, default);

        Assert.AreEqual(problem == null, readiness.Available);
        Assert.AreEqual(problem, readiness.Problem);
        Assert.IsFalse(readiness.CanWaitForCapacity);
        CollectionAssert.AreEqual(Enumerable.Repeat("auth status", malformedReplies == 0 ? 1 : 2).ToArray(),
            _native.Calls.Select(c => string.Join(' ', c)).ToArray());
    }

    [TestMethod]
    public async Task UnreadableStatusKeepsAQueuedTransferWaitingUntilSpaceIsAvailable()
    {
        await Login();
        _native.Storage = """{"used":"3 B","total":"4 B"}""";
        var waits = 0;
        await QueueOriginal(() =>
        {
            Assert.AreEqual(0, _native.Mkdirs);
            Assert.AreEqual(0, _native.Restores);
            switch (++waits)
            {
                case 1:
                    _native.Calls.Clear();
                    _native.MalformedStatusReplies = 2;
                    break;
                case 2:
                    CollectionAssert.AreEqual(new[] {"auth status", "auth status"},
                        _native.Calls.Select(c => string.Join(' ', c)).ToArray());
                    _native.Storage = """{"used":"0 B","total":"4 B"}""";
                    break;
                default:
                    Assert.Fail("The transfer should resume once space is available.");
                    break;
            }
            return Task.CompletedTask;
        });

        Assert.AreEqual(2, waits);
        Assert.AreEqual(1, _native.Mkdirs);
        Assert.AreEqual(1, _native.Restores);
    }

    [TestMethod]
    public async Task QueuedOriginalReadsOnlyCapacityUntilItFitsAndRestoresOnce()
    {
        var account = await Login();
        _native.Storage = """{"used":"3 B","total":"4 B","in_trash":"3 B"}""";
        var waits = 0;
        string? originalJournal = null;
        var result = await QueueOriginal(async () =>
        {
            var journal = Directory.GetFiles(Path.Combine(_root, "appdata", "operations"), "transfer.json",
                SearchOption.AllDirectories).Single();
            var snapshot = await File.ReadAllTextAsync(journal);
            if (++waits == 1)
            {
                originalJournal = snapshot;
            }
            else
            {
                Assert.AreEqual(originalJournal, snapshot);
                CollectionAssert.AreEqual(new[] {"auth status"}, _native.Calls.Select(c => string.Join(' ', c)).ToArray());
                Assert.IsTrue(waits <= 3);
                _native.Storage = waits == 2 ? "{}" : """{"used":"0 B","total":"4 B"}""";
            }
            Assert.AreEqual(0, _native.Mkdirs);
            Assert.AreEqual(0, _native.Restores);
            // A user can inspect the provider's live capacity while the download is waiting.
            var capacity = await _accounts.GetCapacityAsync(account.AccountId, account.AccountVersion, default)
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(waits == 2 ? "unavailable" : "fresh", capacity.Status);
            _native.Calls.Clear();
        });

        Assert.AreEqual(3, waits);
        Assert.AreEqual(1, _native.Mkdirs);
        Assert.AreEqual(1, _native.Restores);
        Assert.AreEqual(1, result.Files.Count);
    }

    [TestMethod]
    public async Task StoppingAQueuedOriginalDoesNotSubmitOrLoseItsPreflightJournal()
    {
        await Login();
        _native.Storage = """{"used":"3 B","total":"4 B"}""";
        using var stop = new CancellationTokenSource();
        try
        {
            await QueueOriginal(() => {stop.Cancel(); return Task.CompletedTask;}, stop.Token);
            Assert.Fail("The cancelled wait should end the native download.");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }

        Assert.AreEqual(0, _native.Mkdirs);
        Assert.AreEqual(0, _native.Restores);
        _native.Storage = """{"used":"0 B","total":"4 B"}""";
        Assert.IsTrue((await _provider.CheckWaitingCapacityAsync(Request, default)).Available);
    }

    [DataTestMethod]
    [DataRow(false, "PIKPAK_SHARE_EXPIRED")]
    [DataRow(true, "PIKPAK_ACCOUNT_CHANGED")]
    public async Task AvailableSpaceCannotResumeAChangedAccountOrExpiredShare(bool replaceAccount, string problem)
    {
        await Login();
        _native.Storage = """{"used":"3 B","total":"4 B"}""";
        var failure = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => QueueOriginal(async () =>
        {
            _native.Storage = """{"used":"0 B","total":"4 B"}""";
            if (replaceAccount)
            {
                await _accounts.LogoutAsync(default);
                await Login();
            }
            else
                _native.OriginalExpired = true;
        }));

        Assert.AreEqual(problem, failure.Message);
        Assert.AreEqual(0, _native.Mkdirs);
        Assert.AreEqual(0, _native.Restores);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AShareLargerThanTheAccountOrAnUncertainRestoreCannotEnterAWait(bool rejectedAfterSubmission)
    {
        await Login();
        _native.Storage = rejectedAfterSubmission ? """{"used":"0 B","total":"4 B"}""" : """{"used":"0 B","total":"2 B"}""";
        _native.RejectRestoreQuota = rejectedAfterSubmission;
        var failure = await Assert.ThrowsExceptionAsync<CloudProviderException>(() =>
            QueueOriginal(() => throw new AssertFailedException("This operation must not wait or retry.")));
        Assert.AreEqual("PIKPAK_STORAGE_QUOTA_EXCEEDED", failure.Message);
        Assert.IsFalse(failure.CanWaitForCapacity);
        Assert.AreEqual(rejectedAfterSubmission ? 1 : 0, _native.Restores);
        Assert.IsFalse((await _provider.CheckWaitingCapacityAsync(Request, default)).CanWaitForCapacity);
    }
}
