using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bootstrap.Models.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed partial class CloudOperationService(BakabaseDbContext db, IEnumerable<ICloudDownloadProvider> providers,
    CloudDownloaderHelper helper, DownloadTaskService tasks, IUserStoragePolicy storage, IServiceProvider? services = null)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    internal static async Task<T> WithOperationLockAsync<T>(string operationId, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(operationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await action(); }
        finally { gate.Release(); }
    }
    private readonly Func<string, IReadOnlyCollection<string>, CancellationToken, Task<ArtifactManifest>> captureManifest =
        (root, files, token) => ArtifactManifestFiles.CaptureAsync(root, files, token);

    internal CloudOperationService(BakabaseDbContext db, IEnumerable<ICloudDownloadProvider> providers,
        CloudDownloaderHelper helper, DownloadTaskService tasks, IUserStoragePolicy storage,
        IServiceProvider? services,
        Func<string, IReadOnlyCollection<string>, CancellationToken, Task<ArtifactManifest>> captureManifest)
        : this(db, providers, helper, tasks, storage, services)
    { this.captureManifest = captureManifest; }
    public ICloudDownloadProvider Provider(string id) => providers.SingleOrDefault(p => p.ProviderId == id && p.MethodId == null)
        ?? throw new InvalidOperationException($"No default download adapter is installed for {id}.");
    public ICloudDownloadProvider Provider(CloudSource source) => DownloadIntents.DownloadProviderRouting.Require(providers, source);
    public static string Key(params string[] parts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(parts)))).ToLowerInvariant();

    public async Task<CloudTransferReceipt> TransferAsync(int runId, CloudDownloadTarget target,
        string accountId, string remoteDirectory, CancellationToken ct, bool noRepeat = false)
    {
        var source = target.SelectedSource ?? throw new ArgumentException("Choose a source first.");
        var id = Key("transfer.v2", CloudSourceIdentity.Key(source, accountId), remoteDirectory);
        if (!await new ArtifactOperationBindingService(db).BindAsync("workflow", runId.ToString(), OperationSlot(target, "transfer"), id, ct))
            throw new InvalidOperationException("The operation input changed. Start another run to use another account or remote directory.");
        var gate = Gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (noRepeat && await db.CloudDownloadOperations.AsNoTracking().AnyAsync(o => o.Id == id && o.State != "succeeded", ct))
                throw new InvalidOperationException("RECOVERY_TRANSFER_REVIEW_REQUIRED");
            var request = new CloudTransferRequest(id, source, accountId, remoteDirectory.TrimEnd('/') + "/" + id);
            var operation = await GetOrCreate(id, runId, target.Id, "transfer", request, ct);
            request = JsonSerializer.Deserialize<CloudTransferRequest>(operation.RequestJson, Json)!;
            if (operation.State == "succeeded")
                return JsonSerializer.Deserialize<CloudTransferReceipt>(operation.ReceiptJson!, Json)!;
            // The remote side may have committed before this process saved its receipt. Never repeat it blindly.
            if (operation.State is "running" or "unknown")
            {
                var recovered = await Provider(source).ReconcileTransferAsync(request, ct);
                if (recovered != null)
                {
                    if (recovered.OperationId != id || recovered.Entry.ProviderId != source.ProviderId || recovered.Entry.AccountId != accountId)
                        throw new InvalidOperationException("The recovered transfer receipt belongs to another operation.");
                    operation.ReceiptJson = JsonSerializer.Serialize(recovered, Json);
                    operation.State = "succeeded";
                    await db.SaveChangesAsync(ct);
                    return recovered;
                }
                throw new InvalidOperationException($"The previous transfer outcome is unknown. Inspect the cloud directory {request.RemoteDirectory} before choosing another source or starting a new transfer.");
            }
            operation.State = "running";
            operation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            try
            {
                var receipt = await Provider(source).TransferAsync(request, ct);
                if (receipt.OperationId != id || receipt.Entry.ProviderId != source.ProviderId || receipt.Entry.AccountId != accountId)
                    throw new InvalidOperationException("The transfer receipt does not match this operation.");
                operation.ReceiptJson = JsonSerializer.Serialize(receipt, Json);
                operation.State = "succeeded";
                await db.SaveChangesAsync(CancellationToken.None);
                return receipt;
            }
            catch (Exception error)
            {
                operation.State = error is CloudProviderException {MayHaveSideEffects: false} ? "failed" : "unknown";
                // Only retain a reviewed pre-write access error, never an upstream exception or secret.
                operation.Error = operation.State == "failed" && Provider(source).CanCorrectAccessCodeAfterFailure(error.Message)
                    ? error.Message : null;
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task<int> EnqueueAsync(int runId, CloudDownloadTarget target, string outputRoot, CancellationToken ct,
        bool existingOnly = false, bool noRepeat = false)
    {
        storage.EnsurePathAllowed(CloudStageDirectoryPath.Download(target) ?? outputRoot);
        var source = target.SelectedSource ?? throw new ArgumentException("Choose a source first.");
        var id = DownloadArtifactCoordinator.DownloadIntent(target, outputRoot, services?.GetService<FileProcessingWorkspaceResolver>());
        if (!await new ArtifactOperationBindingService(db).BindAsync("workflow", runId.ToString(), OperationSlot(target, "download"), id, ct))
            throw new InvalidOperationException("The operation input changed. Start another run to use another source or output directory.");
        var gate = Gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (existingOnly || noRepeat)
            {
                // Diagnostic continuation cannot create a new generation or restart a failed download.
                var existing = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
                if (existing != null)
                {
                    if (existing.DownloadTaskId is not { } existingId)
                        throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
                    var current = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == existingId, ct);
                    if (current?.Status == DownloadTaskDbModelStatus.Complete && await HasValidResult(existingId, ct) ||
                        noRepeat && !existingOnly && current?.Status == DownloadTaskDbModelStatus.InProgress)
                        return existingId;
                    throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
                }
                if (existingOnly) throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
                // No durable download operation exists: the ordinary code below may perform its
                // first enqueue. Subsequent entries into this gate only reuse that same identity.
            }
            var directory = CloudStageDirectoryPath.Download(target) != null
                ? await CloudStageDirectoryPath.PrepareDownloadAsync(target, storage,
                    services?.GetService<FileProcessingWorkspaceResolver>(), ct)
                : target.DownloadDirectoryScope is { } scope
                ? await (services?.GetService<FileProcessingWorkspaceResolver>() ??
                    throw new InvalidOperationException("Task directory allocation is unavailable.")).EnsureScopedDirectoryAsync(scope, ct)
                : Path.Combine(outputRoot, target.StoragePolicy == null ? "cloud-" + id[..16] :
                    Bakabase.Modules.Workflow.Artifacts.ArtifactStoragePolicies.RenderDownload(target.StoragePolicy, target.Title, id, source.ProviderId));
            storage.EnsurePathAllowed(directory);
            var processingDirectory = target.ProcessingDirectoryScope is { } processingScope
                ? await (services?.GetService<FileProcessingWorkspaceResolver>() ??
                    throw new InvalidOperationException("Task directory allocation is unavailable.")).EnsureScopedDirectoryAsync(processingScope, ct)
                : target.StoragePolicy?.ProcessingWorkspaceDirectory;
            var request = new CloudDownloadRequest(id, source, target.AccountId ?? source.AccountId, target.RemoteEntry, directory)
                {WorkspaceDirectory = processingDirectory};
            var operation = await GetOrCreate(id, runId, target.Id, "download", request, ct);
            request = JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, Json)!;
            directory = CloudDownloadDestinations.ExecutionDirectory(request);
            if (operation.DownloadTaskId is { } previousId)
            {
                var previous = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == previousId, ct);
                if (previous == null || previous.Status == DownloadTaskDbModelStatus.Complete && !await HasValidResult(previousId, ct))
                {
                    if (CloudStageDirectoryPath.Download(target) != null)
                        throw new InvalidOperationException("DOWNLOAD_DESTINATION_COMPLETED_OUTPUT_CHANGED");
                    if (request.DestinationRevisions is {Count: > 0})
                        throw new InvalidOperationException("DOWNLOAD_DESTINATION_COMPLETED_OUTPUT_CHANGED");
                    // A missing/modified completed output is a new acquisition generation. Never
                    // overwrite the old directory: a user may have replaced one of its files.
                    directory = Path.Combine(request.OutputDirectory, Guid.NewGuid().ToString("N"));
                    request = request with {OutputDirectory = directory};
                    operation.RequestJson = JsonSerializer.Serialize(request, Json);
                    operation.DownloadTaskId = null;
                    operation.State = "planned";
                    await db.SaveChangesAsync(ct);
                }
            }
            if (operation.DownloadTaskId is not { } taskId)
            {
                var input = new DownloadTaskAddInputModel
                {
                    ThirdPartyId = CloudSites.Site(source.ProviderId), Type = 1,
                    Keys = [source.ProviderId == "exHentai" ? source.Url : source.Id], Names = [target.Title ?? source.ProviderId], DownloadPath = directory,
                    // Freeze content mode; fetching a gallery must not silently complete with only its torrent metadata
                    // or run a second global result workflow beside this workflow's publication/materialization chain.
                    Options = source.ProviderId == "exHentai"
                        ? "{\"downloadMode\":1,\"preferTorrent\":false,\"downloadResultWorkflowId\":null}"
                        : JsonSerializer.Serialize(request, Json), AutoRetry = false
                };
                var built = source.ProviderId == "exHentai"
                    ? await (services?.GetRequiredService<ExHentaiDownloaderHelper>() ?? throw new InvalidOperationException("The site downloader is not registered.")).BuildTasks(input)
                    : await helper.BuildTasks(input);
                var task = built.Single().ToDbModel()!;
                task.Status = DownloadTaskDbModelStatus.Disabled;
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                db.DownloadTasks.Add(task);
                await db.SaveChangesAsync(ct);
                operation.DownloadTaskId = taskId = task.Id;
                operation.State = "queued";
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                // DownloadTaskService uses detached queue snapshots when starting. Do not leave
                // this newly inserted instance tracked when it attaches that task for its status update.
                db.Entry(task).State = EntityState.Detached;
            }
            var row = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId, ct)
                ?? throw new InvalidOperationException("The associated download was deleted.");
            if (row.Status == DownloadTaskDbModelStatus.Disabled && operation.State == "queued")
            {
                operation.State = "starting";
                await db.SaveChangesAsync(ct);
                var response = await tasks.Start(t => t.Id == taskId, targeted: true);
                if (response.Code != (int)ResponseCode.Success && response.Code != (int)ResponseCode.Conflict)
                {
                    operation.State = "queued";
                    await db.SaveChangesAsync(CancellationToken.None);
                    throw new InvalidOperationException(response.Message ?? "The cloud download could not start.");
                }
                operation.State = "started";
                await db.SaveChangesAsync(CancellationToken.None);
            }
            return taskId;
        }
        finally { gate.Release(); }
    }

    private async Task<bool> HasValidResult(int taskId, CancellationToken ct)
    {
        var result = await db.DownloadResults.AsNoTracking().Where(r => r.DownloadTaskId == taskId)
            .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        if (result == null) return false;
        return await HasValidResult(result, captureManifest, ct);
    }

    internal static async Task<bool> HasValidResult(DownloadResultDbModel result,
        Func<string, IReadOnlyCollection<string>, CancellationToken, Task<ArtifactManifest>> capture,
        CancellationToken ct)
    {
        const string unavailable = "The existing download storage could not be verified. Reconnect it or restore read access before continuing; no new download was started.";
        if (!Directory.Exists(result.DownloadDirectory))
            throw new UserStoragePathException(unavailable);
        try
        {
            var files = JsonSerializer.Deserialize<List<string>>(result.FilesJson) ?? [];
            var manifest = await capture(result.DownloadDirectory, files, ct);
            return manifest.Fingerprint == result.Fingerprint;
        }
        catch (FileNotFoundException error)
        {
            if (ArtifactManifestFiles.IsMissingFileConfirmed(result.DownloadDirectory, error.FileName, ct)) return false;
            throw new UserStoragePathException(unavailable);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Unknown storage failures must not clear the completed operation and enqueue
            // another generation. Keep its files, task identity and request available for retry.
            throw new UserStoragePathException(unavailable);
        }
    }

    private async Task<CloudOperationDbModel> GetOrCreate<T>(string id, int runId, string targetId,
        string kind, T request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, Json);
        var operation = await db.CloudDownloadOperations.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (operation != null)
        {
            if (kind == "transfer" && request is CloudTransferRequest transfer &&
                JsonSerializer.Deserialize<CloudTransferRequest>(operation.RequestJson, Json) is { } frozen &&
                (transfer.Source.AccessCodeRevisions?.Count > 0 || frozen.Source.AccessCodeRevisions?.Count > 0) &&
                operation.RequestJson != json)
                throw new InvalidOperationException("This existing transfer belongs to another credential decision. Its saved request is preserved.");
            if (operation.State == "failed" && operation.Kind == "transfer" && operation.DownloadTaskId == null)
            {
                operation.RequestJson = json;
                await db.SaveChangesAsync(ct);
            }
            // Stable keys are made from adapter-normalized resource identity and effective
            // scope. Keep the original frozen transport request (including expiring URLs) on retry.
            if (kind is not ("transfer" or "download") && operation.RequestJson != json)
                throw new InvalidOperationException("The operation input changed.");
            return operation;
        }
        operation = new() {Id = id, WorkflowRunId = runId, TargetId = targetId, Kind = kind, RequestJson = json};
        db.CloudDownloadOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        return operation;
    }
}
