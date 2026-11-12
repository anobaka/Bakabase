using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests;

public sealed partial class CloudWorkflowTests
{
    private static CloudSourceCredentialCorrection Correction(CloudDownloadTarget target) => new()
    {
        CorrectionId = "d3c79beb08e24764a7f7697962d32aa2", TargetId = target.Id, SourceId = target.SelectedSourceId!,
        ExpectedSourceIdentity = CloudSourceIdentity.Key(target.SelectedSource!), NewAccessCode = "fp9q",
        Basis = "sourceEvidence", EvidenceReference = "source:task:fixture:revision:3", Evidence = "Saved source states the access code."
    };

    [TestMethod]
    public async Task Workflow_resume_preserves_the_source_revision_in_its_next_checkpoint()
    {
        var target = Chosen(Source("resume-code", "baidu") with {AccessCode = "old", ArchivePassword = "keep"}, "account-a");
        var workset = Workset(target.SelectedSource!) with {Targets = [target]};
        _baidu.TransferError = new CloudProviderException("BAIDU_ACCESS_CODE_REQUIRED");
        var activity = new BaiduTransferActivity();
        var waiting = await activity.ProcessItemAsync(Context(workset), workset, default);
        Assert.IsNotNull(waiting.Suspension);
        var checkpoint = Item(waiting);
        var correction = Correction(checkpoint.Targets.Single());
        _baidu.TransferError = null;
        var completed = await activity.ResumeAsync(Context(checkpoint), checkpoint,
            JsonSerializer.Serialize(new CloudSelectionSignal {SourceCredentialCorrections = [correction]}, JsonSerializerOptions.Web), default);
        Assert.IsNull(completed.Suspension);
        var next = Item(completed).Targets.Single();
        Assert.IsNotNull(next.RemoteEntry);
        Assert.AreEqual("fp9q", next.SelectedSource!.AccessCode);
        Assert.AreEqual("keep", next.SelectedSource.ArchivePassword);
        Assert.AreEqual(correction.CorrectionId, next.SelectedSource.AccessCodeRevisions!.Single().CorrectionId);
        Assert.AreEqual(2, await Db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(2, _baidu.TransferRequests.Count);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Corrections_check_shared_operations_even_when_first_created_by_another_run()
    {
        var source = Source("shared-code", "baidu") with {AccessCode = "old"};
        var target = Chosen(source, "account-a") with {Problem = "BAIDU_ACCESS_CODE_REQUIRED"};
        _baidu.TransferError = new CloudProviderException(target.Problem);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(71, target, "account-a", "/Bakabase", default));
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(72, target, "account-a", "/Bakabase", default));
        var operation = await Db.CloudDownloadOperations.SingleAsync();
        Assert.AreEqual(71, operation.WorkflowRunId);
        operation.State = "unknown";
        await Db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.ApplyCredentialCorrectionsAsync(72,
            "baidu.transfer", new() {Targets = [target]}, [Correction(target)], default));
        Assert.AreEqual(2, await Db.Set<ArtifactOperationBindingDbModel>().CountAsync());
        Assert.AreEqual(1, await Db.CloudDownloadOperations.CountAsync());
    }

    [DataTestMethod]
    [DataRow("succeeded", false)]
    [DataRow("succeeded", true)]
    [DataRow("unknown", false)]
    [DataRow("running", true)]
    public async Task Lost_checkpoint_recovers_the_exact_saved_decision_without_a_signal_or_new_transfer(string state, bool canReconcile)
    {
        var target = Chosen(Source("lost-checkpoint", "baidu") with {AccessCode = "old", ArchivePassword = "keep"}, "account-a")
            with {Problem = "BAIDU_ACCESS_CODE_REQUIRED"};
        var old = Workset(target.SelectedSource!) with {Targets = [target]};
        _baidu.TransferError = new CloudProviderException(target.Problem);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, target, "account-a", "/Bakabase", default));
        var correction = Correction(target);
        var accepted = await Operations.ApplyCredentialCorrectionsAsync(7201, "baidu.transfer", old, [correction], default);
        _baidu.TransferError = null;
        var firstOwner = state == "succeeded" && canReconcile ? 71 : 7201;
        var receipt = await Operations.TransferAsync(firstOwner, accepted.Targets.Single(), "account-a", "/Bakabase", default);
        if (firstOwner != 7201) await Operations.TransferAsync(7201, accepted.Targets.Single(), "account-a", "/Bakabase", default);
        var operation = await Db.CloudDownloadOperations.SingleAsync(o => o.Id == receipt.OperationId);
        operation.State = state;
        if (state != "succeeded") operation.ReceiptJson = null;
        await Db.SaveChangesAsync();
        _baidu.CanReconcile = canReconcile;

        // Simulate restart: pending signal has been cleared and only the OLD item survived.
        var resumed = await new BaiduTransferActivity().ResumeAsync(Context(old), old, "{}", default);
        var result = Item(resumed).Targets.Single();
        Assert.AreEqual("old", old.Targets.Single().SelectedSource!.AccessCode);
        Assert.AreEqual("fp9q", result.SelectedSource!.AccessCode);
        Assert.AreEqual(accepted.Targets.Single().SelectedSource!.AccessCodeRevisions!.Single(), result.SelectedSource.AccessCodeRevisions!.Single());
        Assert.AreEqual(2, _baidu.TransferRequests.Count, "Recovery must not start another remote write.");
        Assert.AreEqual(state == "succeeded" ? 0 : 1, _baidu.Reconciliations);
        Assert.AreEqual(state == "unknown", resumed.Suspension != null);
        if (state != "unknown") Assert.AreEqual(receipt.Entry, result.RemoteEntry);
        Assert.AreEqual(2, await Db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());

        // Explicit retry of the same correction against the stale checkpoint also restores it.
        var explicitReplay = await Operations.ApplyCredentialCorrectionsAsync(7201, "baidu.transfer", old, [correction], default);
        Assert.AreEqual(JsonSerializer.Serialize(result.SelectedSource), JsonSerializer.Serialize(explicitReplay.Targets.Single().SelectedSource));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.ApplyCredentialCorrectionsAsync(7201,
            "baidu.transfer", old, [correction with {CorrectionId = Guid.NewGuid().ToString("N")}], default));
    }

    [TestMethod]
    public async Task Historical_shared_credential_slots_are_checked_before_another_correction()
    {
        var target = Chosen(Source("historical-shared", "baidu") with {AccessCode = "old"}, "account-a")
            with {Problem = "BAIDU_ACCESS_CODE_REQUIRED"};
        _baidu.TransferError = new CloudProviderException(target.Problem);
        var item = Workset(target.SelectedSource!) with {Targets = [target]};
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(71, target, "account-a", "/Bakabase", default));
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, target, "account-a", "/Bakabase", default));
        var first = await Operations.ApplyCredentialCorrectionsAsync(7201, "baidu.transfer", item, [Correction(target)], default);
        var firstTarget = first.Targets.Single() with {Problem = target.Problem};
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(71, firstTarget, "account-a", "/Bakabase", default));
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, firstTarget, "account-a", "/Bakabase", default));
        var sharedId = (await Db.CloudDownloadOperations.AsNoTracking().ToListAsync()).Single(o => o.RequestJson.Contains("fp9q")).Id;
        var secondDecision = Correction(firstTarget) with {CorrectionId = Guid.NewGuid().ToString("N"), NewAccessCode = "abcd"};
        var second = await Operations.ApplyCredentialCorrectionsAsync(7201, "baidu.transfer", first with {Targets = [firstTarget]}, [secondDecision], default);
        var secondTarget = second.Targets.Single() with {Problem = target.Problem};
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, secondTarget, "account-a", "/Bakabase", default));
        var shared = await Db.CloudDownloadOperations.SingleAsync(o => o.Id == sharedId);
        Assert.AreEqual(71, shared.WorkflowRunId);
        shared.State = "unknown"; await Db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.ApplyCredentialCorrectionsAsync(7201,
            "baidu.transfer", second with {Targets = [secondTarget]},
            [Correction(secondTarget) with {CorrectionId = Guid.NewGuid().ToString("N"), NewAccessCode = "efgh"}], default));
        var transferCount = _baidu.TransferRequests.Count;
        var savedCheckpoint = second with {Targets = [secondTarget]};
        var resumed = await new BaiduTransferActivity().ResumeAsync(Context(savedCheckpoint), savedCheckpoint, "{}", default);
        Assert.IsNotNull(resumed.Suspension);
        Assert.AreEqual("abcd", Item(resumed).Targets.Single().SelectedSource!.AccessCode);
        Assert.AreEqual(transferCount, _baidu.TransferRequests.Count);
    }

    [TestMethod]
    public async Task Lost_corrected_download_checkpoint_waits_instead_of_enqueuing_a_duplicate()
    {
        var target = Chosen(Source("download-crash", "gofile") with {AccessCode = "old"});
        var old = Workset(target.SelectedSource!) with {Targets = [target]};
        var accepted = await Operations.ApplyCredentialCorrectionsAsync(7201, "download.enqueueSupported", old,
            [Correction(target) with {NewAccessCode = null}], default);
        var downloadId = await Operations.EnqueueAsync(7201, accepted.Targets.Single(), old.OutputDirectory, default);
        var resumed = await new EnqueueCloudSourcesActivity().ResumeAsync(Context(old), old, "{}", default);
        Assert.IsNotNull(resumed.Suspension);
        Assert.AreEqual("old", Item(resumed).Targets.Single().SelectedSource!.AccessCode);
        Assert.AreEqual(1, await Db.DownloadTasks.CountAsync());
        Assert.AreEqual(downloadId, (await Db.DownloadTasks.SingleAsync()).Id);
    }

    [DataTestMethod]
    [DataRow("different-source")]
    [DataRow("missing-operation")]
    [DataRow("different-account")]
    [DataRow("planned-with-receipt")]
    public async Task Unverifiable_corrected_binding_never_replays_the_original_request(string mismatch)
    {
        var target = Chosen(Source("invalid-recovery", "baidu") with {AccessCode = "old"}, "account-a")
            with {Problem = "BAIDU_ACCESS_CODE_REQUIRED"};
        var old = Workset(target.SelectedSource!) with {Targets = [target]};
        _baidu.TransferError = new CloudProviderException(target.Problem);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, target, "account-a", "/Bakabase", default));
        var accepted = await Operations.ApplyCredentialCorrectionsAsync(7201, "baidu.transfer", old, [Correction(target)], default);
        _baidu.TransferError = null;
        var receipt = await Operations.TransferAsync(7201, accepted.Targets.Single(), "account-a", "/Bakabase", default);
        var operation = await Db.CloudDownloadOperations.SingleAsync(o => o.Id == receipt.OperationId);
        var request = JsonSerializer.Deserialize<CloudTransferRequest>(operation.RequestJson, JsonSerializerOptions.Web)!;
        if (mismatch == "missing-operation") Db.CloudDownloadOperations.Remove(operation);
        else if (mismatch == "planned-with-receipt") operation.State = "planned";
        else operation.RequestJson = JsonSerializer.Serialize(mismatch == "different-account"
            ? request with {AccountId = "different-account"}
            : request with {Source = request.Source with {Url = "https://pan.baidu.com/s/different"}}, JsonSerializerOptions.Web);
        await Db.SaveChangesAsync();
        var outcome = await new BaiduTransferActivity().ResumeAsync(Context(old), old, "{}", default);
        Assert.IsNotNull(outcome.Suspension);
        Assert.AreEqual("old", Item(outcome).Targets.Single().SelectedSource!.AccessCode);
        Assert.IsNull(Item(outcome).Targets.Single().SelectedSource!.AccessCodeRevisions);
        Assert.AreEqual(2, _baidu.TransferRequests.Count);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Credential_cycles_and_other_decisions_never_overwrite_a_frozen_failed_request()
    {
        var target = Chosen(Source("preserve-frozen", "baidu") with {AccessCode = "old"}, "account-a")
            with {Problem = "BAIDU_ACCESS_CODE_REQUIRED"};
        var old = Workset(target.SelectedSource!) with {Targets = [target]};
        var correction = Correction(target);
        _baidu.TransferError = new CloudProviderException(target.Problem);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, target, "account-a", "/Bakabase", default));
        var accepted = await Operations.ApplyCredentialCorrectionsAsync(7201, "baidu.transfer", old, [correction], default);
        var revised = accepted.Targets.Single() with {Problem = target.Problem};
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, revised, "account-a", "/Bakabase", default));
        var operation = (await Db.CloudDownloadOperations.AsNoTracking().ToListAsync()).Single(o => o.RequestJson.Contains("fp9q"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.ApplyCredentialCorrectionsAsync(7201,
            "baidu.transfer", accepted with {Targets = [revised]},
            [Correction(revised) with {CorrectionId = Guid.NewGuid().ToString("N"), NewAccessCode = "old"}], default));
        var different = revised.SelectedSource! with {AccessCodeRevisions =
            [revised.SelectedSource.AccessCodeRevisions!.Single() with {CorrectionId = Guid.NewGuid().ToString("N")} ]};
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.TransferAsync(72,
            revised with {Sources = [different]}, "account-a", "/Bakabase", default));
        Assert.AreEqual(operation.RequestJson, (await Db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id)).RequestJson);
        Assert.AreEqual(2, _baidu.TransferRequests.Count);
    }

    [TestMethod]
    public async Task Divergent_saved_credential_decisions_require_review_and_never_choose_one_arbitrarily()
    {
        var target = Chosen(Source("divergent", "baidu") with {AccessCode = "old"}, "account-a")
            with {Problem = "BAIDU_ACCESS_CODE_REQUIRED"};
        var old = Workset(target.SelectedSource!) with {Targets = [target]};
        _baidu.TransferError = new CloudProviderException(target.Problem);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, target, "account-a", "/Bakabase", default));
        var first = await Operations.ApplyCredentialCorrectionsAsync(7201, "baidu.transfer", old, [Correction(target)], default);
        var second = await Operations.ApplyCredentialCorrectionsAsync(7201, "baidu.transfer", old,
            [Correction(target) with {CorrectionId = Guid.NewGuid().ToString("N"), NewAccessCode = "abcd"}], default);
        _baidu.TransferError = null;
        // Deliberately seed inconsistent historical branches; recovery must not pick either.
        await Operations.TransferAsync(7201, first.Targets.Single(), "account-a", "/Bakabase", default);
        await Operations.TransferAsync(7201, second.Targets.Single(), "account-a", "/Bakabase", default);
        var resumed = await new BaiduTransferActivity().ResumeAsync(Context(old), old, "{}", default);
        Assert.IsNotNull(resumed.Suspension);
        Assert.IsNull(Item(resumed).Targets.Single().SelectedSource!.AccessCodeRevisions);
        Assert.AreEqual("old", Item(resumed).Targets.Single().SelectedSource!.AccessCode);
        Assert.AreEqual(3, _baidu.TransferRequests.Count);
    }

    [TestMethod]
    public async Task Lost_checkpoint_after_a_second_correction_recovers_the_next_saved_decision()
    {
        var target = Chosen(Source("second-checkpoint", "baidu") with {AccessCode = "old"}, "account-a")
            with {Problem = "BAIDU_ACCESS_CODE_REQUIRED"};
        var old = Workset(target.SelectedSource!) with {Targets = [target]};
        _baidu.TransferError = new CloudProviderException(target.Problem);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, target, "account-a", "/Bakabase", default));
        var first = await Operations.ApplyCredentialCorrectionsAsync(7201, "baidu.transfer", old, [Correction(target)], default);
        var firstTarget = first.Targets.Single() with {Problem = target.Problem};
        first = first with {Targets = [firstTarget]};
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(7201, firstTarget, "account-a", "/Bakabase", default));
        var second = await Operations.ApplyCredentialCorrectionsAsync(7201, "baidu.transfer", first,
            [Correction(firstTarget) with {CorrectionId = Guid.NewGuid().ToString("N"), NewAccessCode = "abcd"}], default);
        _baidu.TransferError = null;
        var receipt = await Operations.TransferAsync(7201, second.Targets.Single(), "account-a", "/Bakabase", default);
        var outcome = await new BaiduTransferActivity().ResumeAsync(Context(first), first, "{}", default);
        Assert.IsNull(outcome.Suspension);
        Assert.AreEqual("abcd", Item(outcome).Targets.Single().SelectedSource!.AccessCode);
        Assert.AreEqual(2, Item(outcome).Targets.Single().SelectedSource!.AccessCodeRevisions!.Count);
        Assert.AreEqual(receipt.Entry, Item(outcome).Targets.Single().RemoteEntry);
        Assert.AreEqual(3, _baidu.TransferRequests.Count);
    }

    [TestMethod]
    public async Task Access_code_correction_retains_the_failed_operation_and_reuses_the_corrected_receipt()
    {
        var original = Source("correction", "baidu") with {AccessCode = "archive-password", ArchivePassword = "archive-password"};
        var target = Chosen(original, "account-a") with {Problem = "BAIDU_ACCESS_CODE_REQUIRED"};
        _baidu.TransferError = new CloudProviderException(target.Problem);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(72, target, "account-a", "/Bakabase", default));
        var previous = await Db.CloudDownloadOperations.AsNoTracking().SingleAsync();
        var item = new DownloadWorkset {Targets = [target]};
        var correction = Correction(target);
        var corrected = await Operations.ApplyCredentialCorrectionsAsync(72, "baidu.transfer", item, [correction], default);
        var revised = corrected.Targets.Single();
        Assert.AreEqual("archive-password", original.AccessCode);
        Assert.AreEqual("fp9q", revised.SelectedSource!.AccessCode);
        Assert.AreEqual(original.ArchivePassword, revised.SelectedSource.ArchivePassword);
        Assert.AreEqual(original.Url, revised.SelectedSource.Url);
        var history = revised.SelectedSource.AccessCodeRevisions!.Single();
        Assert.AreEqual(original.AccessCode, history.PreviousAccessCode);
        Assert.AreEqual(correction.EvidenceReference, history.EvidenceReference);
        Assert.AreEqual(correction.Evidence, history.Evidence);
        Assert.AreEqual(correction.CorrectionId, history.CorrectionId);
        _baidu.TransferError = null;
        var first = await Operations.TransferAsync(72, revised, "account-a", "/Bakabase", default);
        var again = await Operations.TransferAsync(72, revised, "account-a", "/Bakabase", default);
        Assert.AreEqual(first, again);
        Assert.AreEqual(2, _baidu.TransferRequests.Count, "One failed validation and one successful corrected transfer; no repeated transfer.");
        var unchanged = await Db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == previous.Id);
        Assert.AreEqual(previous.RequestJson, unchanged.RequestJson);
        Assert.AreEqual(previous.State, unchanged.State);
        Assert.AreEqual(2, await Db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(2, await Db.Set<ArtifactOperationBindingDbModel>().CountAsync());
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        Assert.IsTrue(_baidu.TransferRequests.Last().Source.AccessCodeRevisions!.Any(r => r.CorrectionId == correction.CorrectionId));
        var repeat = await Operations.ApplyCredentialCorrectionsAsync(72, "download.awaitCompletion",
            corrected with {Targets = [revised with {RemoteEntry = first.Entry}]}, [correction], default);
        Assert.AreEqual(1, repeat.Targets.Single().SelectedSource!.AccessCodeRevisions!.Count);
        Assert.AreEqual(first.Entry, repeat.Targets.Single().RemoteEntry);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Operations.ApplyCredentialCorrectionsAsync(72,
            "baidu.transfer", corrected, [correction with {NewAccessCode = "changed"}], default));
        var metadata = corrected.GetWorkflowOutputMetadata("baidu.transfer").Single(o => o.Key.EndsWith(":accessCodeRevisions"));
        Assert.AreEqual("workflow.nodeOutput.sourceCredentialsCorrected", metadata.LabelKey);
        Assert.AreEqual("1", metadata.Value);
        Assert.IsFalse(JsonSerializer.Serialize(metadata).Contains("fp9q"));
        var resolved = await new DownloadSourceResolver([]).ResolveAsync(revised.SelectedSource, default);
        CollectionAssert.AreEqual(revised.SelectedSource.AccessCodeRevisions, resolved.AccessCodeRevisions);
    }

    [DataTestMethod]
    [DataRow("unknown", false)]
    [DataRow("running", false)]
    [DataRow("succeeded", false)]
    [DataRow("failed", true)]
    public async Task Access_code_correction_rejects_uncertain_successful_or_partial_durable_operations(string state, bool receipt)
    {
        var source = Source("unsafe-correction", "baidu") with {AccessCode = "old"};
        var target = Chosen(source, "account-a") with {Problem = "BAIDU_ACCESS_CODE_REQUIRED"};
        _baidu.TransferError = new CloudProviderException(target.Problem);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(72, target, "account-a", "/Bakabase", default));
        var operation = await Db.CloudDownloadOperations.SingleAsync();
        operation.State = state; operation.ReceiptJson = receipt ? "{}" : null;
        await Db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.ApplyCredentialCorrectionsAsync(72,
            "baidu.transfer", new() {Targets = [target]}, [Correction(target)], default));
        Assert.AreEqual(1, _baidu.TransferRequests.Count);
        Assert.AreEqual(1, await Db.Set<ArtifactOperationBindingDbModel>().CountAsync());
    }

    [DataTestMethod]
    [DataRow("BAIDU_TRANSFER_FAILED_CONFIRM_TARGET")]
    [DataRow("BAIDU_SHARE_UNAVAILABLE")]
    [DataRow("generic failure")]
    public async Task Failed_alone_does_not_prove_an_access_code_can_be_changed(string problem)
    {
        var target = Chosen(Source("unclassified", "baidu") with {AccessCode = "old"}, "account-a") with {Problem = problem};
        _baidu.TransferError = new CloudProviderException(problem);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(72, target, "account-a", "/Bakabase", default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.ApplyCredentialCorrectionsAsync(72,
            "baidu.transfer", new() {Targets = [target]}, [Correction(target)], default));
        Assert.AreEqual("failed", (await Db.CloudDownloadOperations.SingleAsync()).State);
        Assert.AreEqual(1, _baidu.TransferRequests.Count);
    }

    [TestMethod]
    public async Task Legacy_failed_access_validation_uses_the_saved_checkpoint_without_mutating_old_history()
    {
        var target = Chosen(Source("legacy-code", "baidu") with {AccessCode = "old"}, "account-a") with {Problem = "BAIDU_SHARE_ACCESS_CODE_REJECTED"};
        _baidu.TransferError = new CloudProviderException(target.Problem);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => Operations.TransferAsync(72, target, "account-a", "/Bakabase", default));
        var operation = await Db.CloudDownloadOperations.SingleAsync(); operation.Error = null; await Db.SaveChangesAsync();
        var corrected = await Operations.ApplyCredentialCorrectionsAsync(72, "baidu.transfer", new() {Targets = [target]}, [Correction(target)], default);
        Assert.AreEqual("fp9q", corrected.Targets.Single().SelectedSource!.AccessCode);
        Assert.AreEqual(target.Problem, corrected.Targets.Single().SelectedSource!.AccessCodeRevisions!.Single().PreviousFailureCode);
        Assert.IsNull((await Db.CloudDownloadOperations.SingleAsync()).Error);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.ApplyCredentialCorrectionsAsync(72,
            "baidu.transfer", new() {Targets = [target with {Problem = null}]}, [Correction(target)], default));
    }

    [TestMethod]
    public async Task Corrections_bind_to_the_selected_source_and_refuse_stale_or_already_acquired_inputs()
    {
        var target = Chosen(Source("binding", "baidu") with {AccessCode = "old"}, "account-a");
        var item = new DownloadWorkset {Targets = [target]};
        var correction = Correction(target);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Operations.ApplyCredentialCorrectionsAsync(72,
            "baidu.transfer", item, [correction with {TargetId = "other"}], default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Operations.ApplyCredentialCorrectionsAsync(72,
            "baidu.transfer", item, [correction with {SourceId = "other"}], default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.ApplyCredentialCorrectionsAsync(72,
            "baidu.transfer", item, [correction with {ExpectedSourceIdentity = new string('0', 64)}], default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.ApplyCredentialCorrectionsAsync(72,
            "download.awaitCompletion", item, [correction], default));
        foreach (var acquired in new[] {target with {DownloadTaskId = 42}, target with {RemoteEntry = new("baidu", "account-a", "/target")},
                     target with {DownloadArtifactId = "artifact"}, target with {Files = ["preserve"]}, target with {TransportTaskId = "http:existing"}})
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Operations.ApplyCredentialCorrectionsAsync(72,
                "baidu.transfer", item with {Targets = [acquired]}, [correction], default));
        Assert.AreEqual(0, _baidu.TransferRequests.Count);
        Assert.AreEqual(0, await Db.CloudDownloadOperations.CountAsync());
    }

    [TestMethod]
    public async Task Manual_pre_download_correction_is_provider_independent_and_does_not_require_a_reason()
    {
        var target = Chosen(Source("manual-code", "gofile") with {AccessCode = "old", ArchivePassword = "keep"});
        var correction = Correction(target) with {Basis = "manual", EvidenceReference = null, Evidence = null, NewAccessCode = null};
        var result = await Operations.ApplyCredentialCorrectionsAsync(72, "download.enqueueSupported", new() {Targets = [target]}, [correction], default);
        Assert.IsNull(result.Targets.Single().SelectedSource!.AccessCode);
        Assert.AreEqual("keep", result.Targets.Single().SelectedSource!.ArchivePassword);
        Assert.AreEqual(0, await Db.CloudDownloadOperations.CountAsync());
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }
}
