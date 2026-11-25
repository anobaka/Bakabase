using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests;

[TestClass, DoNotParallelize]
public sealed partial class CloudDownloadDestinationTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext _db = null!;
    private string _root = null!;
    private string _oldRoot = null!;
    private string _newRoot = null!;
    private CloudDownloadTarget _target = null!;
    private WorkflowRunDbModel _run = null!;
    private DownloadTaskDbModel _download = null!;
    private CloudOperationDbModel _operation = null!;
    private CloudOperationDbModel _transfer = null!;
    private readonly Provider _provider = new();
    private readonly Guard _guard = new();
    private CloudDownloadDestinationService Service => new(_db, [_provider], new UserStoragePolicy(false),
        _services.GetRequiredService<DownloaderManager>(), _guard);
    private string NewDirectory => Path.Combine(_newRoot, "task");
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
    private Task<CloudDownloadDestinationPreview> Preview() => Service.PreviewAsync(_run.Id, _target, _oldRoot, _newRoot, NewDirectory, default);
    private Task<CloudDownloadDestinationRevisionReceipt> Apply(CloudDownloadDestinationPreview preview, string? revision = null,
        Func<CancellationToken, Task>? owner = null) => Service.ApplyAsync(_run.Id, _target, _oldRoot, _newRoot,
        NewDirectory, preview.Fingerprint!, revision ?? Guid.NewGuid().ToString("N"), default, owner);

    [TestInitialize]
    public async Task Setup()
    {
        _provider.Problem = null; _provider.Checks = 0; _provider.DuringCheck = null; _guard.Reject = false;
        _services = await TestServiceBuilder.BuildServiceProvider(services => services.AddTransient(sp =>
            new BakabaseLocalizer(sp.GetRequiredService<IStringLocalizer<SharedResource>>())));
        _db = _services.GetRequiredService<BakabaseDbContext>();
        _root = Path.Combine(Path.GetTempPath(), "destination-" + Guid.NewGuid().ToString("N"));
        _oldRoot = Path.Combine(_root, "old"); _newRoot = Path.Combine(_root, "new");
        Directory.CreateDirectory(_oldRoot); Directory.CreateDirectory(_newRoot);
        var source = new CloudSource {Id = "source", ProviderId = "baidu", Url = "https://pan.baidu.com/s/fixture", AccountId = "account"};
        _target = new() {Id = "part", AccountId = "account", SelectedSourceId = source.Id, Sources = [source],
            RemoteEntry = new("baidu", "account", "/saved/owned", "[{\"path\":\"/saved/owned/file\",\"size\":4}]")};
        _run = new() {Status = WorkflowRunStatus.Waiting, CurrentItemJson = "unchanged", PlanSnapshotJson = "unchanged"};
        _db.WorkflowRuns.Add(_run); await _db.SaveChangesAsync();
        var intent = DownloadArtifactCoordinator.DownloadIntent(_target, _oldRoot);
        var request = new CloudDownloadRequest(intent, source, "account", _target.RemoteEntry, Path.Combine(_oldRoot, "original"));
        _download = new() {ThirdPartyId = CloudSites.Site("baidu"), Status = DownloadTaskDbModelStatus.Failed,
            Message = "BAIDU_OUTPUT_DIRECTORY_FAILED", DownloadPath = request.OutputDirectory, Options = Json(request), AutoRetry = false};
        _db.DownloadTasks.Add(_download); await _db.SaveChangesAsync();
        _target = _target with {DownloadTaskId = _download.Id, DownloadOperationId = intent};
        _operation = new() {Id = intent, Kind = "download", State = "started", DownloadTaskId = _download.Id,
            WorkflowRunId = _run.Id, TargetId = _target.Id, RequestJson = _download.Options};
        _transfer = new() {Id = "transfer", Kind = "transfer", State = "succeeded", WorkflowRunId = _run.Id,
            TargetId = _target.Id, RequestJson = Json(new CloudTransferRequest("transfer", source, "account", "/saved")),
            ReceiptJson = Json(new CloudTransferReceipt("transfer", _target.RemoteEntry!))};
        _db.CloudDownloadOperations.AddRange(_operation, _transfer);
        _db.ArtifactOperationBindings.AddRange(new() {Id = "download-binding", OwnerKind = "workflow", OwnerId = _run.Id.ToString(),
            Slot = CloudOperationService.OperationSlot(_target, "download"), OperationId = intent},
            new() {Id = "transfer-binding", OwnerKind = "workflow", OwnerId = _run.Id.ToString(),
                Slot = CloudOperationService.OperationSlot(_target, "transfer"), OperationId = _transfer.Id});
        await _db.SaveChangesAsync();
    }

    [TestCleanup]
    public void Cleanup() {(_services as IDisposable)?.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true);}

    [TestMethod]
    public async Task PreviewIsReadOnlyAndApplyPreservesSemanticIdentityTransferAndFailureHistory()
    {
        var oldRequest = _operation.RequestJson;
        var transfer = Json(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _transfer.Id));
        var run = Json(await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == _run.Id));
        var preview = await Preview(); Assert.IsTrue(preview.CanRevise, preview.ReasonCode);
        Assert.AreEqual(oldRequest, (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _operation.Id)).RequestJson);
        Assert.IsFalse(Directory.Exists(NewDirectory)); Assert.IsFalse(Directory.Exists(_download.DownloadPath));
        var receipt = await Apply(preview);
        var changed = await _db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == _download.Id);
        var operation = await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _operation.Id);
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, JsonSerializerOptions.Web)!;
        Assert.AreEqual(_download.Id, receipt.DownloadTaskId); Assert.AreEqual(_operation.Id, receipt.OperationId);
        Assert.AreEqual(_download.DownloadPath, request.OutputDirectory); Assert.AreEqual(NewDirectory, changed.DownloadPath);
        Assert.AreEqual(operation.RequestJson, changed.Options); Assert.AreEqual(_download.Message, changed.Message);
        Assert.AreEqual(DownloadTaskDbModelStatus.Failed, changed.Status); Assert.AreEqual("started", operation.State);
        Assert.AreEqual(transfer, Json(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _transfer.Id)));
        Assert.AreEqual(run, Json(await _db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == _run.Id)));
        Assert.AreEqual(_operation.Id, DownloadArtifactCoordinator.DownloadIntent(_target, _oldRoot));
        Assert.AreEqual(NewDirectory, CloudDownloadDestinations.ExecutionRequest(request).OutputDirectory);
        Assert.IsNull(CloudDownloadDestinations.ExecutionRequest(request).DestinationRevisions);
        Assert.IsFalse(Directory.Exists(NewDirectory)); Assert.AreEqual(1, await _db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task RetryingTheSameRevisionAfterReloadReturnsTheOriginalReceiptWithoutRewritingHistory()
    {
        var preview = await Preview(); var id = Guid.NewGuid().ToString("N"); var first = await Apply(preview, id);
        _db.ChangeTracker.Clear(); var before = Json(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _operation.Id));
        var second = await Apply(preview, id); Assert.IsTrue(second.AlreadyApplied); Assert.AreEqual(first.Revision, second.Revision);
        Assert.AreEqual(before, Json(await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _operation.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(preview with {Fingerprint = new string('b',64)}, id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ApplyAsync(_run.Id+1,_target,_oldRoot,_newRoot,NewDirectory,preview.Fingerprint!,id,default));
    }

    [DataTestMethod]
    [DataRow("file")]
    [DataRow("zero-byte")]
    [DataRow("hidden-checkpoint")]
    [DataRow("empty-child-directory")]
    [DataRow("target-existing")]
    public async Task AnyExistingEntryIsProtectedIncludingZeroByteHiddenOrEmptyDirectory(string kind)
    {
        var directory = kind == "target-existing" ? NewDirectory : _download.DownloadPath;
        Directory.CreateDirectory(directory);
        if (kind == "empty-child-directory") Directory.CreateDirectory(Path.Combine(directory,"child"));
        else await File.WriteAllTextAsync(Path.Combine(directory, kind == "hidden-checkpoint" ? ".download" : "file"), kind == "zero-byte" ? "" : "preserve");
        Assert.AreEqual("DOWNLOAD_DESTINATION_HAS_OUTPUT", (await Preview()).ReasonCode);
        Assert.AreEqual(_download.DownloadPath, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).DownloadPath);
    }

    [DataTestMethod]
    [DataRow("progress")]
    [DataRow("checkpoint")]
    [DataRow("completed")]
    [DataRow("active")]
    [DataRow("run-active")]
    [DataRow("pending-answer")]
    [DataRow("artifact")]
    public async Task ExecutionOrOutputEvidenceRejectsRevision(string kind)
    {
        switch(kind)
        {
            case "progress": _download.Progress = 0.1m; break;
            case "checkpoint": _download.Checkpoint = "unknown"; break;
            case "completed": _download.CompletedAt = DateTime.UtcNow; break;
            case "active": _download.Status = DownloadTaskDbModelStatus.InProgress; break;
            case "run-active": _run.Status = WorkflowRunStatus.Running; break;
            case "pending-answer": _run.PendingSignalJson = "{}"; break;
            case "artifact": _target = _target with {DownloadArtifactId = "registered"}; break;
        }
        await _db.SaveChangesAsync(); Assert.IsFalse((await Preview()).CanRevise);
    }

    [TestMethod]
    public async Task SharedDestinationOrTransferIdentityChangesRemainProtected()
    {
        _db.DownloadTasks.Add(new() {DownloadPath = _download.DownloadPath, Options = "{}", Status = DownloadTaskDbModelStatus.Failed});
        await _db.SaveChangesAsync(); Assert.AreEqual("DOWNLOAD_DESTINATION_SHARED", (await Preview()).ReasonCode);
        _db.DownloadTasks.RemoveRange(await _db.DownloadTasks.Where(d => d.Id != _download.Id).ToListAsync());
        _transfer.State = "unknown"; await _db.SaveChangesAsync();
        Assert.AreEqual("DOWNLOAD_DESTINATION_TRANSFER_REVIEW_REQUIRED", (await Preview()).ReasonCode);
    }

    [TestMethod]
    public async Task StalePreviewAndLateOwnerRejectionLeaveBothPersistentRequestsUnchanged()
    {
        var preview = await Preview(); _download.Message = "changed"; await _db.SaveChangesAsync();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Apply(preview));
        preview = await Preview(); var before = _operation.RequestJson;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Apply(preview, owner:_ => throw new InvalidOperationException("owner changed")));
        Assert.AreEqual(before, (await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _operation.Id)).RequestJson);
        Assert.AreEqual(_download.DownloadPath, (await _db.DownloadTasks.AsNoTracking().SingleAsync()).DownloadPath);
    }

    [TestMethod]
    public async Task FileCreatedDuringProviderInspectionAndUnavailableStorageFailClosed()
    {
        _provider.DuringCheck = () => {Directory.CreateDirectory(_download.DownloadPath); File.WriteAllText(Path.Combine(_download.DownloadPath,"new"),"keep");return Task.CompletedTask;};
        Assert.AreEqual("DOWNLOAD_DESTINATION_HAS_OUTPUT", (await Preview()).ReasonCode);
        _provider.DuringCheck = null; Directory.Delete(_download.DownloadPath,true); Directory.Delete(_oldRoot);
        Assert.AreEqual("DOWNLOAD_DESTINATION_STORAGE_UNAVAILABLE", (await Preview()).ReasonCode);
    }

    [TestMethod]
    public async Task ReserveAndProviderStateAreReportedWithoutStartingOrChangingTheTask()
    {
        _guard.Reject = true; Assert.AreEqual("LOCAL_STORAGE_RESERVE_LOW", (await Preview()).ReasonCode);
        _guard.Reject = false; _provider.Problem = "DOWNLOAD_DESTINATION_PROVIDER_STATE_PRESENT";
        Assert.AreEqual(_provider.Problem, (await Preview()).ReasonCode);
        Assert.AreEqual(_operation.Id, _guard.LastContext?.OwnerId);
        Assert.IsNull(_guard.LastContext?.MinimumFreeBytes, "Configured acquisition reserve must remain the policy source.");
    }

    [TestMethod]
    public async Task LegacyMissingOperationHintUsesTheExclusivePersistedBinding()
    {
        _target = _target with {DownloadOperationId = null};
        var preview = await Preview(); Assert.IsTrue(preview.CanRevise, preview.ReasonCode);
        var receipt = await Apply(preview); Assert.AreEqual(_operation.Id, receipt.OperationId);
        _target = _target with {DownloadOperationId = "different-intent"};
        Assert.AreEqual("DOWNLOAD_DESTINATION_IDENTITY_CHANGED", (await Preview()).ReasonCode);
    }

    [TestMethod]
    public async Task RecoveryUsesPersistedPhysicalOverrideAfterOriginalRootIsUnavailable()
    {
        var preview=await Preview();await Apply(preview);Directory.Delete(_oldRoot);
        var recovery=new CloudDownloadRecoveryService(_db,[_provider],new UserStoragePolicy(false));
        var check=await recovery.CheckAsync(_run.Id,_target,_oldRoot,false,default);
        Assert.IsTrue(check.Candidate,check.ReasonCode);
        Assert.AreEqual(NewDirectory,_provider.LastRetainedRequest?.OutputDirectory);
        Assert.IsNull(_provider.LastRetainedRequest?.DestinationRevisions);
    }

    [TestMethod]
    public async Task AvailableUnmanagedAncestorProtectsOnlyItsVerifiedMembersNotUnlistedSiblingDirectories()
    {
        var entries = new[] {new ArtifactManifestEntry("sibling/file.bin", 1, new string('a',64))};
        var manifest = new ArtifactManifest(entries, ArtifactManifestFiles.Fingerprint(entries));
        var artifact = new ArtifactDbModel {Id="shared-ancestor",RootPath=_newRoot,Managed=false,
            State=ArtifactLocationState.Available,Fingerprint=manifest.Fingerprint,ManifestJson=Json(manifest)};
        _db.Artifacts.Add(artifact);await _db.SaveChangesAsync();
        var preview=await Preview();Assert.IsTrue(preview.CanRevise,preview.ReasonCode);
        var before=Json(await _db.Artifacts.AsNoTracking().SingleAsync());await Apply(preview);
        Assert.AreEqual(before,Json(await _db.Artifacts.AsNoTracking().SingleAsync()));
        Assert.IsFalse(Directory.Exists(NewDirectory));
    }

    [DataTestMethod]
    [DataRow("file-inside")]
    [DataRow("file-equal")]
    [DataRow("directory-equal")]
    [DataRow("directory-inside")]
    [DataRow("managed")]
    [DataRow("same-root")]
    [DataRow("nested-root")]
    [DataRow("modified")]
    [DataRow("quarantine-fence")]
    [DataRow("traversal")]
    [DataRow("bad-fingerprint")]
    [DataRow("bad-json")]
    [DataRow("empty-manifest")]
    public async Task SharedAncestorManifestCannotHideRealOwnershipOrUnverifiableEvidence(string mode)
    {
        var relative=mode switch {"file-inside"=>"task/file.bin","file-equal"=>"task","traversal"=>"../outside",_=>"sibling/file.bin"};
        var entries=mode=="empty-manifest"?Array.Empty<ArtifactManifestEntry>():[new ArtifactManifestEntry(relative,1,new string('a',64))];
        string[]? directories=mode switch {"directory-equal"=>["task"],"directory-inside"=>["task/empty"],_=>null};
        var manifest=new ArtifactManifest(entries,ArtifactManifestFiles.Fingerprint(entries,directories),directories);
        var artifact=new ArtifactDbModel {Id="protected-ancestor",RootPath=mode switch {"same-root"=>NewDirectory,"nested-root"=>Path.Combine(NewDirectory,"nested"),_=>_newRoot},
            State=mode=="modified"?ArtifactLocationState.Modified:ArtifactLocationState.Available,Managed=mode=="managed",
            Fingerprint=mode=="bad-fingerprint"?new string('b',64):manifest.Fingerprint,
            ManifestJson=mode=="bad-json"?"invalid":Json(manifest),QuarantineSourceRootPath=mode=="quarantine-fence"?_newRoot:null};
        _db.Artifacts.Add(artifact);await _db.SaveChangesAsync();
        Assert.IsFalse((await Preview()).CanRevise);
        Assert.AreEqual(_download.DownloadPath,(await _db.DownloadTasks.AsNoTracking().SingleAsync()).DownloadPath);
        Assert.IsFalse(Directory.Exists(NewDirectory));
    }

    [TestMethod]
    public async Task EmptyButRegisteredArtifactAndRegularFileParentAreProtected()
    {
        _db.Artifacts.Add(new() {Id="other",RootPath=NewDirectory,State=Bakabase.Modules.Workflow.Artifacts.ArtifactLocationState.Available});
        await _db.SaveChangesAsync();Assert.AreEqual("DOWNLOAD_DESTINATION_SHARED",(await Preview()).ReasonCode);
        _db.Artifacts.RemoveRange(_db.Artifacts);await _db.SaveChangesAsync();
        File.WriteAllText(Path.Combine(_newRoot,"parent"),"keep");
        var check=await Service.PreviewAsync(_run.Id,_target,_oldRoot,_newRoot,Path.Combine(_newRoot,"parent","child"),default);
        Assert.AreEqual("DOWNLOAD_DESTINATION_PATH_INVALID",check.ReasonCode);
    }

    [DataTestMethod]
    [DataRow(".paths-v1.json")]
    [DataRow(".reuse-v1.json")]
    [DataRow(".reuse-v1.json.lock")]
    public void BaiduProviderEvidenceIsNeverRewrittenEvenWhenNoPayloadExists(string suffix)
    {
        var account = Path.Combine(_root,"account");Directory.CreateDirectory(account);
        Assert.IsTrue(BaiduCloudDownloadProvider.InspectPathJournal(account,"intent").Available);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("intent"))).ToLowerInvariant();
        Directory.CreateDirectory(Path.Combine(account,"operations"));var path=Path.Combine(account,"operations",key+suffix);File.WriteAllText(path,"original");
        Assert.AreEqual("DOWNLOAD_DESTINATION_PROVIDER_STATE_PRESENT",BaiduCloudDownloadProvider.InspectPathJournal(account,"intent").Problem);
        Assert.AreEqual("original",File.ReadAllText(path));
    }

    [TestMethod]
    public void LegacySerializationAndBrokenRevisionChainsRemainDistinct()
    {
        var request=JsonSerializer.Deserialize<CloudDownloadRequest>(_download.Options!,JsonSerializerOptions.Web)!;
        Assert.IsFalse(Json(request).Contains("destinationRevisions"));
        var revision=new CloudDownloadDestinationRevision(Guid.NewGuid().ToString("N"),request.OutputDirectory,NewDirectory,_newRoot,DateTime.UtcNow,new string('a',64));
        var revised=request with {DestinationRevisions=[revision]};Assert.AreEqual(NewDirectory,CloudDownloadDestinations.ExecutionDirectory(revised));
        Assert.ThrowsException<InvalidDataException>(()=>CloudDownloadDestinations.ExecutionDirectory(revised with {DestinationRevisions=[revision with {PreviousDirectory=_newRoot}]}));
        Assert.ThrowsException<InvalidDataException>(()=>CloudDownloadDestinations.ExecutionDirectory(revised with {DestinationRevisions=[revision with {Directory=_oldRoot}]}));
        Assert.ThrowsException<InvalidDataException>(()=>CloudDownloadDestinations.ExecutionDirectory(revised with {DestinationRevisions=[revision,revision]}));
    }

    [DataTestMethod]
    [DataRow(DownloadTaskDbModelStatus.Failed)]
    [DataRow(DownloadTaskDbModelStatus.Disabled)]
    public async Task RetainedSourceRevisionKeepsTheSameSiteAvailableForExplicitWorkflowResume(DownloadTaskDbModelStatus status)
    {
        Directory.CreateDirectory(_download.DownloadPath);
        var original = Path.Combine(_download.DownloadPath, "file");
        await File.WriteAllTextAsync(original, "original");
        await _db.DownloadTasks.Where(d => d.Id == _download.Id).ExecuteUpdateAsync(set => set
            .SetProperty(d => d.Status, status).SetProperty(d => d.Progress, 42m)
            .SetProperty(d => d.LastFileVerificationJson, "previous"));
        var normal = await Preview();
        Assert.IsFalse(normal.CanRevise);
        Assert.IsTrue(normal.CanPreviewReuse);
        var partial = await Service.PreviewAsync(_run.Id, _target, _oldRoot, _newRoot, NewDirectory, default, true);
        Assert.IsTrue(partial.CanRevise, partial.ReasonCode);
        Assert.AreEqual(70L, partial.ReuseEstimate?.RequiredDownloadBytes);
        Assert.AreEqual(100L, _guard.ExpectedBytes);
        var revision = Guid.NewGuid().ToString("N");
        var receipt = await Service.ApplyAsync(_run.Id, _target, _oldRoot, _newRoot, NewDirectory,
            partial.Fingerprint!, revision, default, reuseExistingFiles: true);
        Assert.AreEqual(_download.Id, receipt.DownloadTaskId);
        var repeated = await Service.ApplyAsync(_run.Id, _target, _oldRoot, _newRoot, NewDirectory,
            partial.Fingerprint!, revision, default, reuseExistingFiles: true);
        Assert.IsTrue(repeated.AlreadyApplied);
        var actual = await _db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == _download.Id);
        var operation = await _db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == _operation.Id);
        Assert.AreEqual(actual.Options, operation.RequestJson);
        Assert.AreEqual(status, actual.Status);
        Assert.IsNull(actual.LastFileVerificationJson);
        Assert.AreEqual(0m, actual.Progress);
        Assert.AreEqual(_download.Message, actual.Message);
        Assert.AreEqual(_download.Id, operation.DownloadTaskId);
        var persisted = JsonSerializer.Deserialize<CloudDownloadRequest>(actual.Options!, JsonSerializerOptions.Web)!;
        var execution = CloudDownloadDestinations.ExecutionRequest(persisted);
        Assert.AreEqual(_operation.Id, execution.OperationId);
        Assert.AreEqual(_operation.Id + ":destination:" + revision, execution.LayoutOperationId);
        Assert.AreEqual(NewDirectory, execution.OutputDirectory);
        Assert.AreEqual(new CloudDownloadReuseSource(_operation.Id, _download.DownloadPath, _provider.ObservedFingerprint), execution.ReuseSource);
        Assert.AreEqual(execution, CloudDownloadDestinations.ExecutionRequest(execution));
        var manual = new CloudManualDownloadResume(_db);
        var workset = new DownloadWorkset { OutputDirectory = _oldRoot, Targets = [_target], NoRepeatExistingDownloads = true };
        var owned = (await manual.ReadMultipleAsync(_run.Id, workset, default))!.Single();
        Assert.AreEqual(_target.Id, owned.TargetId);
        Assert.AreEqual($"site:{_download.Id}", owned.DownloadId);
        Assert.AreEqual(status == DownloadTaskDbModelStatus.Disabled ? "stopped" : "failed", owned.Status);
        Assert.IsTrue(owned.CanRetry);
        var foreign = (await manual.ReadMultipleAsync(_run.Id + 1, workset, default))!.Single();
        Assert.IsFalse(foreign.CanRetry);
        Assert.IsNull(await manual.ReadMultipleAsync(_run.Id, workset with { NoRepeatExistingDownloads = false }, default));
        Assert.IsFalse(Directory.Exists(NewDirectory));
        Assert.AreEqual("original", await File.ReadAllTextAsync(original));
        Assert.IsFalse(await _db.DownloadResults.AnyAsync());
    }

    [TestMethod]
    public async Task RetainedSourceStyleCannotBypassSharedRootsAndChangedSourceCannotApply()
    {
        await _db.DownloadTasks.Where(d => d.Id == _download.Id).ExecuteUpdateAsync(set => set.SetProperty(d => d.Progress, 42m));
        var preview = await Service.PreviewAsync(_run.Id, _target, _oldRoot, _newRoot, NewDirectory, default, true);
        Assert.IsTrue(preview.CanRevise, preview.ReasonCode);
        _provider.ObservedFingerprint = new('b', 64);
        var rejected = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Service.ApplyAsync(_run.Id,
            _target, _oldRoot, _newRoot, NewDirectory, preview.Fingerprint!, Guid.NewGuid().ToString("N"), default, reuseExistingFiles: true));
        Assert.AreEqual("DOWNLOAD_DESTINATION_PREVIEW_CHANGED", rejected.Message);
        _db.DownloadTasks.Add(new() {DownloadPath = Path.Combine(_download.DownloadPath, "another-task")});
        await _db.SaveChangesAsync();
        var shared = await Preview();
        Assert.IsFalse(shared.CanPreviewReuse);
        Assert.AreEqual("DOWNLOAD_DESTINATION_SHARED", shared.ReasonCode);
        Assert.IsFalse((await Service.PreviewAsync(_run.Id, _target, _oldRoot, _newRoot, NewDirectory, default, true)).CanRevise);
        Assert.AreEqual(_download.DownloadPath, (await _db.DownloadTasks.AsNoTracking().SingleAsync(d => d.Id == _download.Id)).DownloadPath);
    }

    [TestMethod]
    public async Task UserMustHaveAccessToTheOriginalSourceBeforeAnyProviderInspection()
    {
        var preview = await Service.PreviewAsync(_run.Id, _target, _oldRoot, _newRoot, NewDirectory, default,
            reuseExistingFiles: true, guard: path => path == _download.DownloadPath ? throw new UnauthorizedAccessException() : Task.CompletedTask);
        Assert.IsFalse(preview.CanRevise);
        Assert.IsNull(preview.PreviousDirectory);
        Assert.IsFalse(preview.CanPreviewReuse);
        Assert.AreEqual(0, _provider.ReuseInspections);
        Assert.AreEqual(0, _provider.Checks);
    }

    [TestMethod]
    public async Task ExistingSourceOfAnotherRevisionCannotBeClaimedAsANewDestination()
    {
        var original = Path.Combine(NewDirectory, "retained");
        var otherDestination = Path.Combine(_newRoot, "unrelated");
        var revision = new CloudDownloadDestinationRevision(Guid.NewGuid().ToString("N"), original, otherDestination, _newRoot,
            DateTime.UtcNow, new('a',64)) {ReuseInventoryFingerprint = new('b',64)};
        var other = new CloudDownloadRequest("another-operation", _target.SelectedSource!, "account", _target.RemoteEntry, original)
            {DestinationRevisions = [revision]};
        _db.DownloadTasks.Add(new() {DownloadPath = otherDestination, Options = Json(other)});
        await _db.SaveChangesAsync();
        var blocked = await Service.PreviewAsync(_run.Id, _target, _oldRoot, _newRoot, NewDirectory, default, true);
        Assert.IsFalse(blocked.CanRevise);
        Assert.AreEqual("DOWNLOAD_DESTINATION_SHARED", blocked.ReasonCode);
        Assert.AreEqual(0, _provider.ReuseInspections);
    }

    [TestMethod]
    public void InaccessibleRetainedSourceKeepsItsIdentityButDisablesAllReadsForDestinationOnlyResume()
    {
        var original = JsonSerializer.Deserialize<CloudDownloadRequest>(_download.Options!, JsonSerializerOptions.Web)!;
        var source = new CloudDownloadReuseSource(original.OperationId, original.OutputDirectory, new('a',64));
        var execution = original with {OutputDirectory = NewDirectory, ReuseSource = source};
        Assert.AreSame(execution, CloudSiteDownloader<BaiduCloudTaskType>.GuardRetainedSource(execution, path =>
            Assert.AreEqual(source.OutputDirectory,path)));
        var disconnected = CloudSiteDownloader<BaiduCloudTaskType>.GuardRetainedSource(execution, _ =>
            throw new Bakabase.Abstractions.Exceptions.UserStoragePathException("Unavailable", mountUnavailable: true));
        Assert.AreEqual(source with {DisableSourceRead = true}, disconnected.ReuseSource);
        Assert.IsTrue(Json(disconnected.ReuseSource).Contains("\"disableSourceRead\":true"));
        Assert.AreEqual(original.OperationId, disconnected.OperationId);
        Assert.AreEqual(NewDirectory, disconnected.OutputDirectory);
        Assert.IsFalse(source.DisableSourceRead);
    }

    private sealed class Provider : ICloudDownloadProvider,ICloudDownloadDestinationRevisionProvider,ICloudRetainedDownloadRecoveryProvider,
        ICloudDownloadSourceReuseProvider
    {
        public string ProviderId=>"baidu";public string? Problem;public int Checks;public Func<Task>? DuringCheck;
        public async Task<CloudReadiness> InspectDestinationRevisionAsync(CloudDownloadRequest request,string newDirectory,CancellationToken ct)
        {Checks++;LastDestinationInspection=request;if(DuringCheck!=null)await DuringCheck();return new(Problem==null,Problem);}
        public string ObservedFingerprint = new('a', 64);
        public int ReuseInspections;
        public CloudDownloadRequest? LastSourceInspection;
        public CloudDownloadRequest? LastDestinationInspection;
        public Action? DuringSourceInspection;
        public Task<CloudDownloadReuseInventory> InspectReuseSourceAsync(CloudDownloadRequest request, CancellationToken ct)
        {
            ReuseInspections++;
            LastSourceInspection = request;
            DuringSourceInspection?.Invoke();
            return Task.FromResult(new CloudDownloadReuseInventory(new(4, 100, 2, 30), ObservedFingerprint));
        }
        public CloudDownloadRequest? LastRetainedRequest;
        public bool CanRevalidateRetainedDownloadFailure(string code)=>code=="BAIDU_OUTPUT_DIRECTORY_FAILED";
        public Task<CloudReadiness> InspectRetainedDownloadAsync(CloudDownloadRequest request,bool verifyRemote,CancellationToken ct)
        {LastRetainedRequest=request;return Task.FromResult(new CloudReadiness(true));}
        public Task<CloudReadiness> CheckReadyAsync(string? accountId,CancellationToken ct)=>throw new AssertFailedException("No login or transport check during directory revision");
        public Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,Func<CloudDownloadProgress,Task> progress,CancellationToken ct)=>throw new AssertFailedException("No payload download");
        public Task<CloudTransferReceipt> TransferAsync(CloudTransferRequest request,CancellationToken ct)=>throw new AssertFailedException("No repeated transfer");
    }
    private sealed class Guard:IStorageWriteGuard
    {
        public bool Reject;public StorageWriteContext? LastContext;
        public long? ExpectedBytes;
        public Task CheckAsync(string path,StorageWriteContext? context,CancellationToken ct=default)
        {LastContext=context;if(Reject)throw new LocalStorageException("LOCAL_STORAGE_RESERVE_LOW");return Task.CompletedTask;}
        public Task CheckAllocationAsync(string path,long bytes,StorageWriteContext? context,CancellationToken ct=default)
        {ExpectedBytes = bytes; return CheckAsync(path, context, ct);}
        public Task<T> RunAsync<T>(IReadOnlyList<string> paths,StorageWriteContext? context,Func<CancellationToken,Task<T>> writer,CancellationToken ct=default)=>throw new AssertFailedException("No filesystem writes");
        public Task CopyAsync(Stream source,Stream destination,string path,StorageWriteContext? context,CancellationToken ct=default)=>throw new AssertFailedException("No copying");
    }
}
