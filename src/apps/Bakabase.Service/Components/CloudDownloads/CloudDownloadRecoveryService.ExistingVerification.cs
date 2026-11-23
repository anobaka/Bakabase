using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed partial class CloudDownloadRecoveryService
{
    internal Task<CloudDownloadRecoveryCheck> CheckSelectedNativeAsync(int runId, CloudDownloadTarget target, string outputRoot,
        bool inspectInventory, CancellationToken ct) =>
        CheckCoreAsync(runId, target, outputRoot, false, false, ct, includeStoppedForVerification: true,
            nativeVerification: inspectInventory ? NativeVerificationScope.InspectInventory : NativeVerificationScope.IdentityOnly);

    public Task VerifyExistingAsync(int runId, CloudDownloadTarget target, string outputRoot,
        DownloadTaskService tasks, CancellationToken ct, Func<Func<Task>, Task>? guardRegistration = null) =>
        VerifySelectedNativeAsync(runId, target, outputRoot, tasks, ct, guardRegistration, null);

    internal async Task VerifySelectedNativeAsync(int runId, CloudDownloadTarget target, string outputRoot,
        DownloadTaskService tasks, CancellationToken ct, Func<Func<Task>, Task>? guardRegistration,
        Func<CancellationToken, Task>? validateSelection)
    {
        async Task Validate(CancellationToken token)
        {
            var check = await CheckAsync(runId, target, outputRoot, false, token, includeStoppedForVerification: true);
            if (!check.Candidate || !check.VerifyExistingOnly)
                throw new InvalidOperationException(check.FailureCode ?? check.ReasonCode);
        }
        var validate = validateSelection ?? Validate;
        await validate(ct);
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, outputRoot, workspaces);
        var operation = await db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == intent, ct);
        var frozenRequest = operation.RequestJson;
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(frozenRequest, JsonSerializerOptions.Web)!;
        var verifier = DownloadProviderRouting.Require(providers, target.SelectedSource!) as ICloudExistingDownloadVerificationProvider
            ?? throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        await tasks.VerifyAndCompleteExistingFilesAsync(target.DownloadTaskId!.Value,
            token => verifier.VerifyExistingDownloadAsync(CloudDownloadDestinations.ExecutionRequest(request), token), async token =>
            {
                await validate(token);
                var latest = await db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id, token);
                if (latest.RequestJson != frozenRequest || latest.DownloadTaskId != operation.DownloadTaskId)
                    throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            }, ct, allowStopped: true, guardRegistration: guardRegistration, workflowRunId: runId);
    }
}
