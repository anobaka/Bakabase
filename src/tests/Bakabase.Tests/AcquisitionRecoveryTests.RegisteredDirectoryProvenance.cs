using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionRecoveryTests
{
    private sealed class RegisteredBaiduInspection(string accountDirectory) : ICloudDownloadDirectoryProvenanceProvider
    {
        public Task<CloudDownloadDirectoryEvidence?> InspectDirectoryEvidenceAsync(CloudDownloadRequest request, string root,
            CancellationToken ct) => BaiduDownloadDirectoryEvidence.ReadAsync(accountDirectory, "17", request, root, ct);
    }

    [DataTestMethod]
    [DataRow("verified")]
    [DataRow("no-journal")]
    [DataRow("no-transfer")]
    [DataRow("different-source")]
    [DataRow("different-account")]
    [DataRow("different-manifest")]
    public async Task Registered_input_recovers_only_the_exact_offline_provider_wrapper_for_its_new_snapshot(string scenario)
    {
        const string transferId = "prior-transfer";
        var digest = ArtifactManifestFiles.Hash(transferId);
        var wrapper = "bakabase-" + digest[..24];
        var f = await RegisteredDownloadFixture(wrapper: wrapper);
        var download = await _db.CloudDownloadOperations.SingleAsync();
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(download.RequestJson, WorkflowJson.Options)!;
        var remotePath = "/original/" + wrapper;
        var manifest = new[] {new {id = 1, path = remotePath + "/author/official.bin",
            size = new FileInfo(f.File).Length, isDirectory = false}};
        var remote = new CloudRemoteEntry(request.Source.ProviderId!, request.AccountId!, remotePath,
            JsonSerializer.Serialize(manifest));
        download.RequestJson = JsonSerializer.Serialize(request with {RemoteEntry = remote}, WorkflowJson.Options);
        var original = new CloudTransferRequest(transferId,
            scenario == "different-source" ? request.Source with {Url = "https://pan.baidu.com/s/foreign"} : request.Source,
            scenario == "different-account" ? "other" : request.AccountId!, remotePath);
        if (scenario != "no-transfer")
        {
            _db.CloudDownloadOperations.Add(new() {Id = transferId, WorkflowRunId = f.RunId, TargetId = download.TargetId,
                Kind = "transfer", State = "succeeded", RequestJson = JsonSerializer.Serialize(original, WorkflowJson.Options),
                ReceiptJson = JsonSerializer.Serialize(new CloudTransferReceipt(transferId, remote), WorkflowJson.Options)});
            Assert.IsTrue(await new ArtifactOperationBindingService(_db).BindAsync("workflow", f.RunId.ToString(),
                download.TargetId + ":transfer", transferId));
        }
        await _db.SaveChangesAsync();
        var journal = Directory.CreateDirectory(Path.Combine(_root, "account", "operations"));
        if (scenario != "no-journal")
            await File.WriteAllTextAsync(Path.Combine(journal.FullName, digest + ".json"), JsonSerializer.Serialize(new
            {stage = "completed", remotePath, manifest = scenario == "different-manifest" ? [] : manifest}));
        var choice = (await f.Plans.PreviewRegisteredDownloadsAsync(f.TaskId, new(1), default)).Candidates.Single();
        var prepared = await f.Plans.ApplyRegisteredDownloadAsync(f.TaskId, new(1, choice.Token), default);
        var state = await _db.AcquisitionTasks.AsNoTracking().Where(t => t.Id == f.TaskId).Select(t => t.BuilderStateJson).SingleAsync();
        var receipt = AcquisitionRegisteredDownload.Read<AcquisitionPlanBuilderState>(state)!.RegisteredDownload!;
        var store = _services.GetRequiredService<IArtifactOperationStore>();
        var input = (await store.GetArtifactAsync(receipt.Input.ArtifactId))!;
        var coordinator = new DownloadArtifactCoordinator(_db, store, new UserStoragePolicy(false),
            _services.GetRequiredService<DownloadResultService>(), directoryProvenance: new(_services,
                [new("baidu", null, _ => new RegisteredBaiduInspection(journal.Parent!.FullName))]));
        var started = await f.Plans.RunAsync(f.TaskId, prepared.Task.PlanRevision, default);
        var run = await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == started.Task.WorkflowRunId);
        var workset = JsonSerializer.Deserialize<DownloadWorkset>(run.PayloadJson!, WorkflowJson.Options)!;
        await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = WorkflowRunPlans.Read(run.PlanSnapshotJson)!.Nodes[0].ConfigJson,
            Services = _services, Logger = NullLogger.Instance}, workset, default);
        var storage = new UserStoragePolicy(false);
        await using var services = new ServiceCollection().AddSingleton(store).AddSingleton(coordinator).BuildServiceProvider();
        var processing = started.Plan!.StageDirectories!.Parts[receipt.PartId].Processing!;
        var snapshot = await AcquisitionProcessingStageInput.PrepareCoreAsync(_db, store, storage,
            new FileProcessingWorkspaceResolver(_root, storage), f.TaskId, run.Id, receipt.PartId, processing, input,
            new FileArtifactMaterializer(), services.GetRequiredService<IServiceScopeFactory>(), default);
        Assert.AreEqual(processing.Directory, snapshot.RootDirectory);
        var projected = FileArtifactDeliveryLayout.Preview(snapshot, new());
        Assert.AreEqual(scenario == "verified" ? "author/official.bin" : wrapper + "/author/official.bin", projected.FileMappings.Single().TargetRelativePath);
        Assert.IsNull(FileArtifactDeliveryLayout.ReadProvenance((await store.GetArtifactAsync(input.Id))!));
        Assert.IsTrue(File.Exists(f.File));
    }
}
