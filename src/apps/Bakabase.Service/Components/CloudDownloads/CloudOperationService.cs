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
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Downloader.Cloud;
using Bootstrap.Models.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed class CloudOperationService(BakabaseDbContext db, IEnumerable<ICloudDownloadProvider> providers,
    CloudDownloaderHelper helper, DownloadTaskService tasks, IUserStoragePolicy storage)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    public ICloudDownloadProvider Provider(string id) => providers.SingleOrDefault(p => p.ProviderId == id)
        ?? throw new InvalidOperationException($"No download adapter is installed for {id}.");
    public static string Key(params string[] parts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(parts)))).ToLowerInvariant();

    public async Task<CloudTransferReceipt> TransferAsync(int runId, CloudDownloadTarget target,
        string accountId, string remoteDirectory, CancellationToken ct)
    {
        var source = target.SelectedSource ?? throw new ArgumentException("Choose a source first.");
        var id = Key(runId.ToString(), target.Id, source.Id, "transfer");
        var gate = Gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var request = new CloudTransferRequest(id, source, accountId, remoteDirectory.TrimEnd('/') + "/" + id);
            var operation = await GetOrCreate(id, runId, target.Id, "transfer", request, ct);
            if (operation.State == "succeeded")
                return JsonSerializer.Deserialize<CloudTransferReceipt>(operation.ReceiptJson!, Json)!;
            // The remote side may have committed before this process saved its receipt. Never repeat it blindly.
            if (operation.State is "running" or "unknown")
            {
                var recovered = await Provider(source.ProviderId).ReconcileTransferAsync(request, ct);
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
                var receipt = await Provider(source.ProviderId).TransferAsync(request, ct);
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
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task<int> EnqueueAsync(int runId, CloudDownloadTarget target, string outputRoot, CancellationToken ct)
    {
        storage.EnsurePathAllowed(outputRoot);
        var source = target.SelectedSource ?? throw new ArgumentException("Choose a source first.");
        var id = Key(runId.ToString(), target.Id, source.Id, "download");
        var gate = Gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var directory = Path.Combine(outputRoot, "cloud-" + id[..16]);
            storage.EnsurePathAllowed(directory);
            var request = new CloudDownloadRequest(id, source, source.ProviderId == "baidu" ? target.AccountId : null, target.RemoteEntry, directory);
            var operation = await GetOrCreate(id, runId, target.Id, "download", request, ct);
            if (operation.DownloadTaskId is not { } taskId)
            {
                var built = await helper.BuildTasks(new DownloadTaskAddInputModel
                {
                    ThirdPartyId = CloudSites.Site(source.ProviderId), Type = 1,
                    Keys = [source.Id], Names = [target.Title ?? source.ProviderId], DownloadPath = directory,
                    Options = JsonSerializer.Serialize(request, Json), AutoRetry = false
                });
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

    private async Task<CloudOperationDbModel> GetOrCreate<T>(string id, int runId, string targetId,
        string kind, T request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, Json);
        var operation = await db.CloudDownloadOperations.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (operation != null)
        {
            if (operation.State == "failed" && operation.Kind == "transfer" && operation.DownloadTaskId == null)
            {
                operation.RequestJson = json;
                await db.SaveChangesAsync(ct);
            }
            if (operation.RequestJson != json) throw new InvalidOperationException("The operation input changed.");
            return operation;
        }
        operation = new() {Id = id, WorkflowRunId = runId, TargetId = targetId, Kind = kind, RequestJson = json};
        db.CloudDownloadOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        return operation;
    }
}
