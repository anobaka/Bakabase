using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private sealed class OnlyDownloadGuard : IStorageWriteGuard
    {
        public int Checks; public bool Reject;
        public Task CheckAsync(string path, StorageWriteContext? owner, CancellationToken ct = default)
        {Checks++; Assert.AreEqual("cloudOperation", owner?.OwnerKind); if(Reject)throw new LocalStorageException("LOCAL_STORAGE_RESERVE_LOW"); return Task.CompletedTask;}
        public Task CheckAllocationAsync(string path,long bytes,StorageWriteContext? owner,CancellationToken ct=default)=>CheckAsync(path,owner,ct);
        public Task<T> RunAsync<T>(IReadOnlyList<string> paths,StorageWriteContext? owner,Func<CancellationToken,Task<T>> writer,CancellationToken ct=default)=>throw new AssertFailedException("No payload");
        public Task CopyAsync(Stream source,Stream target,string path,StorageWriteContext? owner,CancellationToken ct=default)=>throw new AssertFailedException("No copy");
    }
    private sealed class OnlyDownloadStarter(AcquisitionRecoveryTests fixture, int runId) : IAcquisitionOriginalDownloadStarter
    {
        public List<int> Calls = []; public int LeaseChecks; public bool LoseResponse; public Func<Task>? BetweenLeases; public Func<Task>? AfterStart;
        public string? PreparedRetryOperationId;
        public bool HandoffPreparedPayloadOnly;
        public Task<bool> StartAsync(int id,Func<CancellationToken,Task> validate,Func<CancellationToken,Task> owner,CancellationToken ct,
            string? preparedRetryOperationId = null, bool handoffPreparedPayloadOnly = false)=>
            fixture._services.GetRequiredService<DownloaderManager>().WithTaskActionLock(id,async()=>
            {
                await validate(ct);
                var gate=WorkflowRunMutationGate.For(runId);await gate.WaitAsync(ct);
                try {await owner(ct);LeaseChecks++;await fixture._db.DownloadTasks.Where(d=>d.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Status,DownloadTaskDbModelStatus.InProgress),ct);}
                finally {gate.Release();}
                if(BetweenLeases!=null)await BetweenLeases();
                await gate.WaitAsync(ct);
                try {await owner(ct);LeaseChecks++;}
                catch {await fixture._db.DownloadTasks.Where(d=>d.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Status,DownloadTaskDbModelStatus.Failed));throw;}
                finally {gate.Release();}
                PreparedRetryOperationId=preparedRetryOperationId;HandoffPreparedPayloadOnly=handoffPreparedPayloadOnly;
                Calls.Add(id);if(LoseResponse)throw new IOException("transport result lost");
                await fixture._db.DownloadTasks.Where(d=>d.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Status,DownloadTaskDbModelStatus.Complete),ct);
                if(AfterStart!=null)await AfterStart();return true;
            });
    }
    private AcquisitionDownloadOnlyRecoveryService OnlyDownloads(CloudDownloadRecoveryService recovery,IAcquisitionOriginalDownloadStarter starter,OnlyDownloadGuard guard)=>
        new(_db,recovery,starter,_services.GetRequiredService<DownloaderManager>(),guard,_services.GetRequiredService<DownloadTaskService>());

    [TestMethod]
    public async Task DownloadsScopeStartsOriginalIdAndPreservesWaitingRunAndNoRepeatCheckpoint()
    {
        var (task,run,_,download,_,_,recovery)=await SeedRecoverableProvider();
        var starter=new OnlyDownloadStarter(this,run.Id);var guard=new OnlyDownloadGuard();var host=new RecordingHost();
        var service=new AcquisitionRecoveryService(_db,_diagnostics,new(),host,downloadOnly:OnlyDownloads(recovery,starter,guard));
        var before=JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync());
        var preview=await service.PreviewAsync(new([task.Id],"downloads"));Assert.AreEqual("downloads",preview.Scope);
        Assert.AreEqual("downloads",preview.Rows.Single().Action);
        var result=await service.ApplyAsync(new(preview.PreviewToken,[task.Id]));
        CollectionAssert.AreEqual(new[]{task.Id},result.ContinuedTaskIds.ToArray());
        CollectionAssert.AreEqual(new[]{download.Id},result.Downloads!.Single().StartedDownloadTaskIds.ToArray());
        Assert.AreEqual(2,starter.LeaseChecks);Assert.IsTrue(guard.Checks>=3);Assert.AreEqual(0,host.Calls);
        Assert.AreEqual(before,JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync()));
        Assert.AreEqual("downloadsOnly", AcquisitionDownloadContinuationService.Read(await _db.AcquisitionTasks.AsNoTracking()
            .SingleAsync(t => t.Id == task.Id))!.Mode);
        await service.ApplyAsync(new(preview.PreviewToken,[task.Id]));Assert.AreEqual(1,starter.Calls.Count);
        var second=await service.PreviewAsync(new([task.Id],"downloads"));Assert.AreEqual(0,second.ContinuableCount);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DownloadsScopeVerifiesAndRegistersExistingFilesWithoutStartingPayloadOrWorkflow(bool matches)
    {
        var (run, target, download, _, _, provider, recovery) = await SeedRetainedDownload();
        download.Message = "An artifact file changed while its manifest was being recorded. (mtime) DownloadResultService.RecordFilesAsync CloudSiteDownloader";
        download.Key = "original";
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with {Targets = [target]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        var beforeRun = JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync());
        var task = await _db.AcquisitionTasks.SingleAsync(t => t.WorkflowRunId == run.Id);
        var file = Path.Combine(download.DownloadPath, "retained-fixture");
        var bytes = await File.ReadAllBytesAsync(file);
        var sha = System.Security.Cryptography.SHA256.HashData(matches ? bytes : "other"u8.ToArray());
        provider.ExistingProof = new([new(file, bytes.Length, Convert.ToHexString(sha))]);
        var starter = new OnlyDownloadStarter(this, run.Id); var host = new RecordingHost(); var writes = new OnlyDownloadGuard {Reject = true};
        var diagnostics = new AcquisitionRecoveryDiagnostics(_db, new UserStoragePolicy(false), _services.GetRequiredService<IArtifactOperationStore>(), downloads: recovery);
        var service = new AcquisitionRecoveryService(_db, diagnostics, new(), host, downloadOnly: OnlyDownloads(recovery, starter, writes));
        var guarded = new List<string?>();
        var preview = await service.PreviewAsync(new([task.Id], "downloads"), guardDirectory: path => { guarded.Add(path); return Task.CompletedTask; });
        var candidate = preview.Rows.Single();
        Assert.IsTrue(candidate.CanContinue, candidate.ReasonCode);
        Assert.AreEqual("RECOVERY_EXISTING_FILES_VERIFY_REQUIRED", candidate.ReasonCode);
        Assert.IsTrue(guarded.Contains(download.DownloadPath));
        Assert.AreEqual(0, provider.VerificationCalls);
        var applied = await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        var outcome = applied.Downloads!.Single();
        Assert.AreEqual(matches, applied.ContinuedTaskIds.Contains(task.Id));
        Assert.AreEqual(matches, outcome.VerifiedDownloadTaskIds!.Contains(download.Id));
        Assert.AreEqual(matches ? null : "RECOVERY_EXISTING_FILES_UNVERIFIED", outcome.ReasonCode);
        Assert.AreEqual(0, outcome.StartedDownloadTaskIds.Count);
        Assert.AreEqual(0, host.Calls); Assert.AreEqual(0, starter.Calls.Count); Assert.AreEqual(0, writes.Checks);
        Assert.AreEqual(matches ? 1 : 0, await _db.DownloadResults.CountAsync());
        Assert.AreEqual(beforeRun, JsonSerializer.Serialize(await _db.WorkflowRuns.AsNoTracking().SingleAsync()));
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(file));
        await service.ApplyAsync(new(preview.PreviewToken, [task.Id]));
        Assert.AreEqual(1, provider.VerificationCalls);
    }

    [TestMethod]
    public async Task DownloadVerificationRechecksWorkflowOwnershipAfterHashingAndBeforeRegistering()
    {
        var (run, target, download, _, _, provider, recovery) = await SeedRetainedDownload();
        download.Message = "An artifact file changed while its manifest was being recorded. (mtime) DownloadResultService.RecordFilesAsync CloudSiteDownloader";
        download.Key = "original";
        var workset = AcquisitionRecoveryDiagnostics.ReadWorkset(run.CurrentItemJson!)! with {Targets = [target]};
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset);
        await _db.SaveChangesAsync();
        var file = Path.Combine(download.DownloadPath, "retained-fixture");
        var bytes = await File.ReadAllBytesAsync(file);
        provider.ExistingProof = new([new(file, bytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)))]);
        var service = OnlyDownloads(recovery, new OnlyDownloadStarter(this, run.Id), new());
        var signal = JsonSerializer.Serialize(new CloudSelectionSignal {ObserveExistingOnly = true,
            VerifyExistingFilesOnly = true, ResumeTargetIds = [target.Id]}, JsonSerializerOptions.Web);
        var checks = 0;
        var result = await service.StartAsync(17, run, signal, _ =>
        {
            if (++checks == 2) throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
            return Task.CompletedTask;
        }, default);
        Assert.AreEqual(1, provider.VerificationCalls);
        Assert.AreEqual("ACQUISITION_TASK_CHANGED", result.ReasonCode);
        Assert.AreEqual(0, result.VerifiedDownloadTaskIds!.Count);
        Assert.AreEqual(0, await _db.DownloadResults.CountAsync());
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).Status);
    }

    [TestMethod]
    public async Task UnknownScopeAndUnavailableDownloadsCannotFallBackToWorkflowResume()
    {
        var (task,_,_)=await Seed();var host=new RecordingHost();var service=new AcquisitionRecoveryService(_db,_diagnostics,new(),host);
        await Assert.ThrowsExceptionAsync<ArgumentException>(()=>service.PreviewAsync(new([task.Id],"anything")));
        var preview=await service.PreviewAsync(new([task.Id],"downloads"));Assert.AreEqual(0,preview.ContinuableCount);
        Assert.AreEqual("RECOVERY_DOWNLOAD_SCOPE_UNAVAILABLE",preview.Rows.Single().ReasonCode);Assert.AreEqual(0,host.Calls);
        Assert.AreEqual("workflow",(await service.PreviewAsync(new([task.Id]))).Scope);
    }

    [DataTestMethod]
    [DataRow("verify-only")]
    [DataRow("missing-target")]
    [DataRow("new-source")]
    [DataRow("not-await")]
    public async Task DownloadsScopeRejectsVerificationOnlyAndChangedSelection(string change)
    {
        var (task,run,_,_,_,_,_)=await SeedRecoverableProvider();
        var diagnosis=await _diagnostics.DiagnoseAsync(task,run,default);
        var signal=JsonSerializer.Deserialize<CloudSelectionSignal>(diagnosis.SignalJson,JsonSerializerOptions.Web)!;
        if(change=="verify-only")signal=signal with {VerifyExistingFilesOnly=true};
        if(change=="missing-target")signal=signal with {ResumeTargetIds=["missing"]};
        if(change=="new-source")signal=signal with {SourceIds=["new"]};
        if(change=="not-await")run.CurrentStepIndex=1;
        Assert.IsNull(AcquisitionDownloadOnlyRecoveryService.Targets(run,JsonSerializer.Serialize(signal,JsonSerializerOptions.Web)));
    }

    [TestMethod]
    public async Task DownloadsScopeDelegatesConcurrencyToDownloadCenterInsteadOfCountingOtherAcquisitions()
    {
        var (task,run,_,download,_,_,recovery)=await SeedRecoverableProvider();
        _services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.Concurrency=1;
        var (_,otherRun,_)=await Seed();
        var queued=new DownloadTaskDbModel {DownloadPath=Path.Combine(_root,"queued"),Status=DownloadTaskDbModelStatus.InProgress};
        _db.DownloadTasks.Add(queued);await _db.SaveChangesAsync();
        _db.CloudDownloadOperations.Add(new(){Id="other-queued",Kind="download",WorkflowRunId=otherRun.Id,TargetId="other",DownloadTaskId=queued.Id});await _db.SaveChangesAsync();
        var starter=new OnlyDownloadStarter(this,run.Id);var host=new RecordingHost();
        var service=new AcquisitionRecoveryService(_db,_diagnostics,new(),host,downloadOnly:OnlyDownloads(recovery,starter,new()));
        var preview=await service.PreviewAsync(new([task.Id],"downloads"));var result=await service.ApplyAsync(new(preview.PreviewToken,[task.Id]));
        CollectionAssert.AreEqual(new[] {task.Id}, result.ContinuedTaskIds.ToArray());
        CollectionAssert.AreEqual(new[] {download.Id}, starter.Calls);
        Assert.AreEqual(DownloadTaskDbModelStatus.Complete, (await _db.DownloadTasks.AsNoTracking().SingleAsync(d=>d.Id==download.Id)).Status);
        Assert.AreEqual(DownloadTaskDbModelStatus.InProgress, (await _db.DownloadTasks.AsNoTracking().SingleAsync(d=>d.Id==queued.Id)).Status);
    }

    [DataTestMethod]
    [DataRow("capacity")]
    [DataRow("owner-changed")]
    [DataRow("lost-response")]
    public async Task DownloadsScopePreservesPartialOrUnknownOutcomesWithoutReplaying(string mode)
    {
        var (task,run,_,download,_,_,recovery)=await SeedRecoverableProvider();
        var starter=new OnlyDownloadStarter(this,run.Id){LoseResponse=mode=="lost-response"};var guard=new OnlyDownloadGuard{Reject=mode=="capacity"};
        if(mode=="owner-changed")starter.BetweenLeases=()=>_db.WorkflowRuns.Where(r=>r.Id==run.Id).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.Status,WorkflowRunStatus.Cancelled));
        var host=new RecordingHost();var service=new AcquisitionRecoveryService(_db,_diagnostics,new(),host,downloadOnly:OnlyDownloads(recovery,starter,guard));
        var preview=await service.PreviewAsync(new([task.Id],"downloads"));var result=await service.ApplyAsync(new(preview.PreviewToken,[task.Id]));
        Assert.AreEqual(0,result.ContinuedTaskIds.Count);Assert.AreEqual(0,host.Calls);
        if(mode=="lost-response")CollectionAssert.AreEqual(new[]{download.Id},result.Downloads!.Single().IndeterminateDownloadTaskIds.ToArray());
        else Assert.AreEqual(DownloadTaskDbModelStatus.Failed,(await _db.DownloadTasks.AsNoTracking().SingleAsync(d=>d.Id==download.Id)).Status);
        await service.ApplyAsync(new(preview.PreviewToken,[task.Id]));Assert.AreEqual(mode=="lost-response"?1:0,starter.Calls.Count);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeConditionalStartRejectsSecondOwnerLeaseWithoutLeavingAutomaticQueue(bool ownerGuard)
    {
        var (task,_,_,download,_,_,_)=await SeedRecoverableProvider();download.Type=1;await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear(); // Exercise the detached production service path, not fixture seed tracking.
        var checks=0;var service=_services.GetRequiredService<DownloadTaskService>();
        Exception? failure=null;string? diagnostic=null;
        try {var result=await service.StartConditionally(download.Id,_=>Task.CompletedTask,default,async _=>
        {checks++;if(ownerGuard&&checks==1)await _db.AcquisitionTasks.Where(t=>t.Id==task.Id).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.DeletedAt,DateTime.Now));
            if(!ownerGuard&&checks==2)throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");});
            diagnostic=$"checks={checks}, code={result.Code}, message={result.Message}";}
        catch(Exception error){failure=error;}
        Assert.IsTrue(failure is InvalidOperationException,$"Expected owner rejection; {diagnostic}; {failure}");
        Assert.AreEqual(ownerGuard?1:2,checks,"Native owner acquisition and the subsequent conditional callback both protect start.");
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed,(await _db.DownloadTasks.AsNoTracking().SingleAsync(d=>d.Id==download.Id)).Status);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DownloadsScopeReportsActualMultiTargetPartialAndDoesNotReplayFirstDownload(bool ownerChanged)
    {
        var (task,run,workset,first,_,_,recovery)=await SeedRecoverableProvider();
        var target=workset.Targets[0] with {Id="second",DownloadTaskId=null,
            Sources=[workset.Targets[0].SelectedSource! with {Url="https://mypikpak.com/s/second"}]};
        var intent=DownloadArtifactCoordinator.DownloadIntent(target,_root);
        var request=new CloudDownloadRequest(intent,target.SelectedSource!,null,null,Path.Combine(_root,"second"));
        var second=new DownloadTaskDbModel {Status=DownloadTaskDbModelStatus.Failed,ThirdPartyId=first.ThirdPartyId,
            Message=first.Message,Options=JsonSerializer.Serialize(request,JsonSerializerOptions.Web),DownloadPath=request.OutputDirectory};
        _db.DownloadTasks.Add(second);await _db.SaveChangesAsync();
        _db.CloudDownloadOperations.Add(new(){Id=intent,Kind="download",State="started",WorkflowRunId=run.Id,TargetId="second",DownloadTaskId=second.Id,RequestJson=second.Options});
        _db.ArtifactOperationBindings.Add(new(){Id="second-binding",OwnerKind="workflow",OwnerId=run.Id.ToString(),Slot="second:download",OperationId=intent});
        run.CurrentItemJson=WorkflowItemSnapshot.Capture(workset with {Targets=[workset.Targets[0],target with {DownloadTaskId=second.Id}]});await _db.SaveChangesAsync();
        var guard=new OnlyDownloadGuard();var starter=new OnlyDownloadStarter(this,run.Id){AfterStart=ownerChanged
            ? ()=>_db.WorkflowRuns.Where(r=>r.Id==run.Id).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.Status,WorkflowRunStatus.Cancelled))
            : ()=>{guard.Reject=true;return Task.CompletedTask;}};
        var service=new AcquisitionRecoveryService(_db,_diagnostics,new(),new RecordingHost(),downloadOnly:OnlyDownloads(recovery,starter,guard));
        var preview=await service.PreviewAsync(new([task.Id],"downloads"));var result=await service.ApplyAsync(new(preview.PreviewToken,[task.Id]));
        Assert.AreEqual(0,result.ContinuedTaskIds.Count);Assert.AreEqual("RECOVERY_DOWNLOAD_PARTIALLY_STARTED",result.Skipped.Single().ReasonCode);
        var outcome=result.Downloads!.Single();CollectionAssert.AreEqual(new[]{first.Id},outcome.StartedDownloadTaskIds.ToArray());
        Assert.AreEqual(ownerChanged?"ACQUISITION_TASK_CHANGED":"LOCAL_STORAGE_RESERVE_LOW",outcome.ReasonCode);Assert.AreEqual(0,outcome.IndeterminateDownloadTaskIds.Count);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed,(await _db.DownloadTasks.AsNoTracking().SingleAsync(d=>d.Id==second.Id)).Status);
        await service.ApplyAsync(new(preview.PreviewToken,[task.Id]));CollectionAssert.AreEqual(new[]{first.Id},starter.Calls);
    }
}
