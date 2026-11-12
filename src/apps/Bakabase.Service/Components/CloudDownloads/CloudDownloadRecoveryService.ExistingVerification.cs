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
    public async Task VerifyExistingAsync(int runId, CloudDownloadTarget target, string outputRoot,
        DownloadTaskService tasks, CancellationToken ct)
    {
        async Task Validate(CancellationToken token)
        {
            var check = await CheckAsync(runId, target, outputRoot, false, token);
            if (!check.Candidate || !check.VerifyExistingOnly)
                throw new InvalidOperationException(check.FailureCode ?? check.ReasonCode);
        }
        await Validate(ct);
        var intent = DownloadArtifactCoordinator.DownloadIntent(target, outputRoot, workspaces);
        var operation = await db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == intent, ct);
        var frozenRequest = operation.RequestJson;
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(frozenRequest, JsonSerializerOptions.Web)!;
        var verifier = DownloadProviderRouting.Require(providers, target.SelectedSource!) as ICloudExistingDownloadVerificationProvider
            ?? throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
        await tasks.VerifyAndCompleteExistingFilesAsync(target.DownloadTaskId!.Value,
            token => verifier.VerifyExistingDownloadAsync(CloudDownloadDestinations.ExecutionRequest(request), token), async token =>
            {
                await Validate(token);
                var latest = await db.CloudDownloadOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id, token);
                if (latest.RequestJson != frozenRequest || latest.DownloadTaskId != operation.DownloadTaskId)
                    throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            }, ct);
    }
}
