using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

public sealed partial class CloudWorkflowTests
{
    [TestMethod]
    public async Task First_transfer_proof_requires_an_unused_frozen_target_and_preserves_first_write_intent()
    {
        var provider = new TransferRecoveryFixture {TransferError = null};
        var operations = TransferRecoveryOperations(provider);
        var target = Chosen(Source("unvisited-sibling", "baidu"), "account-a");
        Assert.AreEqual(CloudTransferRecoveryDisposition.FirstAttempt,
            (await operations.InspectTransferRecoveryAsync(73, target, "account-a", "/Bakabase", true, default)).Disposition);
        Assert.AreEqual(CloudTransferRecoveryDisposition.Unavailable,
            (await operations.InspectTransferRecoveryAsync(73, target, "account-b", "/Bakabase", true, default)).Disposition);
        Assert.AreEqual(0, await Db.CloudDownloadOperations.CountAsync());
        Assert.HasCount(0, provider.Requests);

        var bindings = new ArtifactOperationBindingService(Db);
        Assert.IsTrue(await bindings.BindAsync("workflow", "74", CloudOperationService.OperationSlot(target, "transfer"),
            "different-operation", default));
        Assert.AreEqual(CloudTransferRecoveryDisposition.Unavailable,
            (await operations.InspectTransferRecoveryAsync(74, target, "account-a", "/Bakabase", true, default)).Disposition);

        var receipt = await operations.TransferAsync(73, target, "account-a", "/Bakabase", default, noRepeat: true);
        Assert.HasCount(1, provider.Requests);
        Assert.AreEqual("succeeded", (await Db.CloudDownloadOperations.SingleAsync(o => o.Id == receipt.OperationId)).State);
        Assert.AreEqual(CloudTransferRecoveryDisposition.Reconcile,
            (await operations.InspectTransferRecoveryAsync(73, target, "account-a", "/Bakabase", true, default)).Disposition);
    }

    [TestMethod]
    public async Task Transfer_recovery_approval_persists_one_isolated_attempt_and_never_replays_unknown_writes()
    {
        var provider = new TransferRecoveryFixture();
        var operations = TransferRecoveryOperations(provider);
        var target = Chosen(Source("recovery-unknown", "baidu"), "account-a");
        await Assert.ThrowsExceptionAsync<IOException>(() => operations.TransferAsync(73, target, "account-a", "/Bakabase", default));
        var original = provider.Requests.Single();
        provider.Evidence = _ => new(CloudTransferRecoveryDisposition.RequiresConfirmation);

        Assert.AreEqual(CloudTransferRecoveryDisposition.Candidate,
            (await operations.InspectTransferRecoveryAsync(73, target, "account-a", "/Bakabase", false, default)).Disposition);
        Assert.AreEqual(CloudTransferRecoveryDisposition.RequiresConfirmation,
            (await operations.InspectTransferRecoveryAsync(73, target, "account-a", "/Bakabase", true, default)).Disposition);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => operations.TransferAsync(73, target, "account-a", "/Bakabase", default, noRepeat: true));
        Assert.HasCount(1, provider.Requests);
        Assert.AreEqual(1, await Db.CloudDownloadOperations.CountAsync());

        Assert.AreEqual(CloudTransferRecoveryDisposition.Authorized,
            (await operations.AuthorizeTransferRecoveryAsync(73, target, "account-a", "/Bakabase", default)).Disposition);
        Assert.HasCount(1, provider.Requests);
        Assert.AreEqual("planned", (await Db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Kind == "transfer-recovery")).State);

        provider.Evidence = _ => new(CloudTransferRecoveryDisposition.Unavailable);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => operations.TransferAsync(73, target, "account-a", "/Bakabase", default, noRepeat: true));
        Assert.HasCount(1, provider.Requests);
        provider.Evidence = _ => new(CloudTransferRecoveryDisposition.RequiresConfirmation);

        // The isolated request also loses its response. It may reconcile an exact receipt
        // later, but a second confirmation cannot allocate another generation.
        await Assert.ThrowsExceptionAsync<IOException>(() => operations.TransferAsync(73, target, "account-a", "/Bakabase", default, noRepeat: true));
        Assert.HasCount(2, provider.Requests);
        var replacement = provider.Requests[1];
        Assert.AreNotEqual(original.OperationId, replacement.OperationId);
        Assert.AreEqual(original.RemoteDirectory, replacement.RemoteDirectory);
        Assert.AreEqual(CloudTransferRecoveryDisposition.Unavailable,
            (await operations.AuthorizeTransferRecoveryAsync(73, target, "account-a", "/Bakabase", default)).Disposition);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => operations.TransferAsync(73, target, "account-a", "/Bakabase", default, noRepeat: true));
        Assert.HasCount(2, provider.Requests);
        Assert.AreEqual(2, await Db.CloudDownloadOperations.CountAsync());

        provider.Evidence = request => request.OperationId == replacement.OperationId
            ? new(CloudTransferRecoveryDisposition.Reconcile, Receipt: TransferRecoveryFixture.Receipt(replacement))
            : new(CloudTransferRecoveryDisposition.RequiresConfirmation);
        var recovered = await operations.TransferAsync(73, target, "account-a", "/Bakabase", default, noRepeat: true);
        Assert.AreEqual(original.OperationId, recovered.OperationId);
        Assert.AreEqual(replacement.OperationId, recovered.Entry.OwnedContainer?.TransferOperationId);
        Assert.AreEqual(recovered, await operations.TransferAsync(73, target, "account-a", "/Bakabase", default, noRepeat: true));
        Assert.HasCount(2, provider.Requests);
    }

    [TestMethod]
    public async Task Continue_allocates_one_previewed_generation_only_after_all_prior_owned_targets_are_empty()
    {
        var provider = new TransferRecoveryFixture();
        var ops = TransferRecoveryOperations(provider);
        var target = Chosen(Source("repeat-empty", "baidu"), "account-a");
        const int run = 73;
        const string directory = "/Bakabase";
        async Task<CloudTransferRecoveryCheck> Inspect() =>
            await ops.InspectTransferRecoveryAsync(run, target, "account-a", directory, true, default);
        async Task<CloudTransferRecoveryCheck> Authorize(string? expected = null) =>
            await ops.AuthorizeTransferRecoveryAsync(run, target, "account-a", directory, default, expected);
        Task<CloudTransferReceipt> Continue() => ops.TransferAsync(run, target, "account-a", directory, default, noRepeat: true);
        await Assert.ThrowsExceptionAsync<IOException>(Continue);
        var empty = new CloudTransferRecoveryCheck(CloudTransferRecoveryDisposition.RequiresConfirmation) {VerifiedEmptyTarget = true};
        provider.Evidence = _ => empty;
        Assert.AreEqual(CloudTransferRecoveryDisposition.Authorized, (await Authorize()).Disposition);
        await Assert.ThrowsExceptionAsync<IOException>(Continue);
        var first = provider.Requests[0];
        var second = provider.Requests[1];
        var next = (await Inspect()).NextIsolatedOperationId;
        Assert.IsNotNull(next);
        Assert.AreNotEqual(first.OperationId, next);
        Assert.AreNotEqual(second.OperationId, next);
        Assert.HasCount(2, provider.Requests);
        Assert.AreEqual(2, await Db.CloudDownloadOperations.CountAsync());

        // The original or a previously authorized generation can change after preview.
        provider.Evidence = request => request.OperationId == first.OperationId ? new(CloudTransferRecoveryDisposition.Unavailable) : empty;
        Assert.AreEqual(CloudTransferRecoveryDisposition.Unavailable, (await Authorize(next)).Disposition);
        provider.Evidence = _ => empty;
        Assert.AreEqual(CloudTransferRecoveryDisposition.Unavailable, (await Authorize("stale")).Disposition);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(Continue);
        Assert.HasCount(2, provider.Requests);
        Assert.AreEqual(CloudTransferRecoveryDisposition.Authorized, (await Authorize(next)).Disposition);
        Assert.AreEqual(CloudTransferRecoveryDisposition.Authorized, (await Authorize(next)).Disposition);
        Assert.AreEqual(3, await Db.CloudDownloadOperations.CountAsync());
        provider.Evidence = request => request.OperationId == second.OperationId ? new(CloudTransferRecoveryDisposition.Unavailable) : empty;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(Continue);
        Assert.HasCount(2, provider.Requests);

        provider.Evidence = _ => empty;
        await Assert.ThrowsExceptionAsync<IOException>(Continue);
        Assert.AreEqual(next, provider.Requests[2].OperationId);
        var later = (await Inspect()).NextIsolatedOperationId;
        Assert.IsNotNull(later);
        Assert.AreNotEqual(later, next);
        Assert.AreEqual(CloudTransferRecoveryDisposition.Unavailable, (await Authorize(next)).Disposition);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(Continue);
        Assert.HasCount(3, provider.Requests);
        Assert.AreEqual(CloudTransferRecoveryDisposition.Authorized, (await Authorize(later)).Disposition);
        provider.TransferError = null;
        var completed = await Continue();
        Assert.AreEqual(first.OperationId, completed.OperationId);
        Assert.AreEqual(later, completed.Entry.OwnedContainer?.TransferOperationId);
        Assert.AreEqual(completed, await Continue());
        Assert.HasCount(4, provider.Requests);
    }

    [TestMethod]
    [DataRow("sharedOwner")]
    [DataRow("downstreamBinding")]
    [DataRow("providerUncertain")]
    [DataRow("confirmedRejection")]
    [DataRow("pendingProviderReport")]
    public async Task Further_transfer_waits_for_exclusive_ownership_no_downstream_work_and_verified_provider_evidence(string conflict)
    {
        var provider = new TransferRecoveryFixture();
        var ops = TransferRecoveryOperations(provider);
        var target = Chosen(Source("blocked-generation", "baidu"), "account-a");
        Task<CloudTransferReceipt> Continue() => ops.TransferAsync(73, target, "account-a", "/Bakabase", default, noRepeat: true);
        await Assert.ThrowsExceptionAsync<IOException>(Continue);
        var verified = new CloudTransferRecoveryCheck(CloudTransferRecoveryDisposition.RequiresConfirmation) {VerifiedEmptyTarget = true};
        provider.Evidence = _ => verified;
        await ops.AuthorizeTransferRecoveryAsync(73, target, "account-a", "/Bakabase", default);
        if (conflict == "confirmedRejection")
        {
            provider.TerminalCode = "TRANSFER_DEFINITIVELY_REJECTED";
            provider.TransferError = new CloudProviderException(provider.TerminalCode, true);
            await Assert.ThrowsExceptionAsync<CloudProviderException>(Continue);
            var failed = await Db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Kind == "transfer-recovery");
            Assert.AreEqual("unknown", failed.State);
            Assert.AreEqual(provider.TerminalCode, failed.Error);
        }
        else await Assert.ThrowsExceptionAsync<IOException>(Continue);

        var bindings = new ArtifactOperationBindingService(Db);
        if (conflict == "sharedOwner")
            Assert.IsTrue(await bindings.BindAsync("workflow", "99", target.Id + ":transfer", provider.Requests[0].OperationId, default));
        if (conflict == "downstreamBinding")
            Assert.IsTrue(await bindings.BindAsync("workflow", "73", target.Id + ":artifact.download", "later", default));
        if (conflict == "providerUncertain")
            provider.Evidence = _ => new(CloudTransferRecoveryDisposition.RequiresConfirmation);
        if (conflict == "pendingProviderReport")
            provider.Evidence = request => request.OperationId == provider.Requests[1].OperationId
                ? new(CloudTransferRecoveryDisposition.Unavailable, "TRANSFER_PENDING_UPSTREAM") : verified;
        var decision = await ops.InspectTransferRecoveryAsync(73, target, "account-a", "/Bakabase", true, default);
        Assert.AreEqual(CloudTransferRecoveryDisposition.Unavailable, decision.Disposition);
        if (conflict == "confirmedRejection") Assert.AreEqual(provider.TerminalCode, decision.Problem);
        if (conflict == "pendingProviderReport") Assert.AreEqual("TRANSFER_PENDING_UPSTREAM", decision.Problem);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(Continue);
        Assert.HasCount(2, provider.Requests);
        Assert.AreEqual(2, await Db.CloudDownloadOperations.CountAsync());
    }

    [TestMethod]
    public async Task Proven_pre_transfer_journal_retries_the_original_from_its_shared_binding()
    {
        var provider = new TransferRecoveryFixture();
        var operations = TransferRecoveryOperations(provider);
        var originalTarget = Chosen(Source("shared-original", "baidu"), "account-a");
        await Assert.ThrowsExceptionAsync<IOException>(() => operations.TransferAsync(73, originalTarget, "account-a", "/Bakabase", default));
        var request = provider.Requests.Single();
        var sharedTarget = originalTarget with {Id = "shared-revision"};
        // Ordinary resume writes an exact current ownership binding before looking up the
        // shared original. Without provider proof this still cannot perform a cloud write.
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => operations.TransferAsync(74, sharedTarget, "account-a", "/Bakabase", default));
        provider.Evidence = _ => new(CloudTransferRecoveryDisposition.Retry);
        provider.TransferError = null;
        Assert.AreEqual(CloudTransferRecoveryDisposition.Retry,
            (await operations.InspectTransferRecoveryAsync(74, sharedTarget, "account-a", "/Bakabase", true, default)).Disposition);
        var recovered = await operations.TransferAsync(74, sharedTarget, "account-a", "/Bakabase", default, noRepeat: true);
        Assert.AreEqual(request.OperationId, provider.Requests[1].OperationId);
        Assert.AreEqual(request.RemoteDirectory, provider.Requests[1].RemoteDirectory);
        Assert.AreEqual(CloudSourceIdentity.Fingerprint(request.Source), CloudSourceIdentity.Fingerprint(provider.Requests[1].Source));
        Assert.AreEqual(request.OperationId, recovered.OperationId);
        Assert.AreEqual(1, await Db.CloudDownloadOperations.CountAsync());
    }

    [TestMethod]
    public async Task A_reviewed_completed_transfer_gets_one_separate_receipt_before_the_first_download()
    {
        var provider = new TransferRecoveryFixture {TransferError = null};
        var operations = TransferRecoveryOperations(provider);
        var target = Chosen(Source("completed-transfer", "baidu"), "account-a");
        var output = Directory.CreateDirectory(Path.Combine(_root, "replay-output")).FullName;
        var old = await operations.TransferAsync(73, target, "account-a", "/Bakabase", default);
        target = target with {RemoteEntry = old.Entry};
        var reviewed = await operations.InspectTransferReplayAsync(73, target, output, "/Bakabase", default);
        Assert.IsNotNull(reviewed);
        Assert.AreNotEqual(old.OperationId, reviewed.Request.OperationId);
        Assert.HasCount(1, provider.Requests);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => operations.ExecutePreparedTransferReplayAsync(
            73, target, output, "/Bakabase", reviewed.Request.OperationId, default));
        Assert.IsNull(await operations.InspectTransferReplayAsync(74, target, output, "/Bakabase", default));
        Assert.IsNull(await operations.InspectTransferReplayAsync(73, target with {RemoteEntry = old.Entry with {Path = "/elsewhere"}},
            output, "/Bakabase", default));
        Assert.IsTrue(await operations.PrepareTransferReplayAsync(73, reviewed, default));
        var next = await operations.ExecutePreparedTransferReplayAsync(73, target, output, "/Bakabase", reviewed.Request.OperationId, default);
        Assert.AreNotEqual(old.Entry.Path, next.Entry.Path);
        Assert.AreEqual(next, await operations.ExecutePreparedTransferReplayAsync(73, target, output, "/Bakabase", reviewed.Request.OperationId, default));
        Assert.AreEqual(old, await operations.TransferAsync(73, target, "account-a", "/Bakabase", default));
        Assert.HasCount(2, provider.Requests);
        Assert.IsNull(await operations.InspectTransferReplayAsync(73, target with {DownloadTaskId = 1234}, output, "/Bakabase", default));
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, output);
        var partDir = Path.Combine(output, "cloud-" + intent[..16]);
        Directory.CreateDirectory(partDir);
        File.WriteAllText(Path.Combine(partDir, "part"), "retained");
        Assert.IsNull(await operations.InspectTransferReplayAsync(73, target, output, "/Bakabase", default));
    }

    private CloudOperationService TransferRecoveryOperations(TransferRecoveryFixture provider) => new(Db, [provider],
        _services.GetRequiredService<CloudDownloaderHelper>(), _services.GetRequiredService<DownloadTaskService>(), _resumeStorage);

    internal sealed class TransferRecoveryFixture : ICloudDownloadProvider, ICloudTransferRecoveryProvider
    {
        public string ProviderId => "baidu";
        public bool SupportsTransfer => true;
        public List<CloudTransferRequest> Requests { get; } = [];
        public string? TerminalCode { get; set; }
        public bool IsTerminalTransferProblem(string problem) => problem == TerminalCode;
        public Exception? TransferError { get; set; } = new IOException("Connection lost.");
        public Func<CloudTransferRequest, CloudTransferRecoveryCheck> Evidence { get; set; } =
            _ => new(CloudTransferRecoveryDisposition.Unavailable);

        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct) => Task.FromResult(new CloudReadiness(true));

        public Task<CloudTransferReceipt> TransferAsync(CloudTransferRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return TransferError == null ? Task.FromResult(Receipt(request)) : Task.FromException<CloudTransferReceipt>(TransferError);
        }

        public Task<CloudTransferRecoveryCheck> InspectTransferRecoveryAsync(CloudTransferRequest request, bool verifyRemote, CancellationToken ct) =>
            Task.FromResult(verifyRemote ? Evidence(request) : new(CloudTransferRecoveryDisposition.Candidate));

        public Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct) =>
            throw new AssertFailedException("Transfer recovery must not directly start a download.");

        public static CloudTransferReceipt Receipt(CloudTransferRequest request) => new(request.OperationId,
            new CloudRemoteEntry("baidu", request.AccountId, request.RemoteDirectory + "/" + request.OperationId)
            {OwnedContainer = new(request.OperationId, request.RemoteDirectory + "/" + request.OperationId)});
    }
}
