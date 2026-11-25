using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.CloudDownloads;

public enum CloudNamingField { FileName }
public enum BaiduCloudTaskType
{
    [Downloader(ThirdPartyId.BaiduNetdisk, typeof(BaiduCloudDownloader), "{FileName}", typeof(CloudNamingField), typeof(CloudDownloaderHelper))]
    RemoteFileOrDirectory = 1
}
public enum GoogleCloudTaskType
{
    [Downloader(ThirdPartyId.GoogleDrive, typeof(GoogleCloudDownloader), "{FileName}", typeof(CloudNamingField), typeof(CloudDownloaderHelper))]
    Share = 1
}
public enum GofileCloudTaskType
{
    [Downloader(ThirdPartyId.Gofile, typeof(GofileCloudDownloader), "{FileName}", typeof(CloudNamingField), typeof(CloudDownloaderHelper))]
    Share = 1
}
public enum PikPakCloudTaskType
{
    [Downloader(ThirdPartyId.PikPak, typeof(PikPakCloudDownloader), "{FileName}", typeof(CloudNamingField), typeof(CloudDownloaderHelper))]
    Share = 1
}
public enum OneDriveCloudTaskType
{
    [Downloader(ThirdPartyId.OneDrive, typeof(OneDriveCloudDownloader), "{FileName}", typeof(CloudNamingField), typeof(CloudDownloaderHelper))]
    Share = 1
}
public enum MegaCloudTaskType
{
    [Downloader(ThirdPartyId.Mega, typeof(MegaCloudDownloader), "{FileName}", typeof(CloudNamingField), typeof(CloudDownloaderHelper))]
    Share = 1
}
public enum PixeldrainCloudTaskType
{
    [Downloader(ThirdPartyId.Pixeldrain, typeof(PixeldrainCloudDownloader), "{FileName}", typeof(CloudNamingField), typeof(CloudDownloaderHelper))]
    Share = 1
}
public enum TeraBoxCloudTaskType
{
    [Downloader(ThirdPartyId.TeraBox, typeof(TeraBoxCloudDownloader), "{FileName}", typeof(CloudNamingField), typeof(CloudDownloaderHelper))]
    Share = 1
}

public static class CloudSites
{
    public sealed record MethodDescriptor(string Id, string DisplayName, string? EngineId, string? AccountProvider,
        IReadOnlyList<string> SourceKinds, IReadOnlyList<string> DependencyIds, string? Scope);
    public sealed record SourceDescriptor(string ProviderId, ThirdPartyId ThirdPartyId)
    {
        public bool SupportsRemote => DownloadIntents.DownloadMethodCatalog.Methods.Any(m => m.ProviderId == ProviderId && m.SupportsRemote);
        public IReadOnlyList<MethodDescriptor> Methods => DownloadIntents.DownloadMethodCatalog.Methods.Where(m => m.ProviderId == ProviderId)
            .Select(m => new MethodDescriptor(m.Id, m.DisplayName, m.EngineId, m.AccountProvider, m.SourceKinds, m.DependencyIds, m.Scope)).ToList();
        public IReadOnlyList<string> DependencyIds =>
            Methods.SelectMany(m => m.DependencyIds).Distinct().ToList();
    }
    public static readonly IReadOnlyList<SourceDescriptor> Sources =
    [
        new("baidu", ThirdPartyId.BaiduNetdisk),
        new("googleDrive", ThirdPartyId.GoogleDrive),
        new("gofile", ThirdPartyId.Gofile),
        new("pikpak", ThirdPartyId.PikPak),
        new("oneDrive", ThirdPartyId.OneDrive),
        new("pixeldrain", ThirdPartyId.Pixeldrain),
        new("mega", ThirdPartyId.Mega),
        new("terabox", ThirdPartyId.TeraBox)
    ];
    public static ThirdPartyId Site(string provider) => provider == "exHentai" ? ThirdPartyId.ExHentai :
        Sources.FirstOrDefault(source => source.ProviderId == provider)?.ThirdPartyId
        ?? throw new ArgumentException("This source has no installed download adapter.");
    public static string Provider(ThirdPartyId site) => Sources.FirstOrDefault(source => source.ThirdPartyId == site)?.ProviderId
        ?? throw new ArgumentException("Choose a cloud download source.");
}

/// <summary>Cloud adapters use the established site queue, lifecycle, file accounting and download center.</summary>
public abstract class CloudSiteDownloader<T>(IServiceProvider services) : AbstractDownloader<T>(services) where T : struct
{
    private DownloadTransferProgress? _transferProgress;
    public override DownloadTransferProgress? TransferProgress => Status == DownloaderStatus.Downloading
        ? Volatile.Read(ref _transferProgress) : null;

    internal static DownloadTransferProgress? TransferSnapshot(CloudDownloadProgress progress, DateTime observedAt)
    {
        var done = progress.CompletedBytes is >= 0 ? progress.CompletedBytes : null;
        var total = progress.TotalBytes is >= 0 ? progress.TotalBytes : null;
        // A contradictory total is unknown; preserve the real transferred count.
        if (done > total) total = null;
        return done == null && total == null ? null : new(done, total, observedAt);
    }

    protected override async Task StartCore(DownloadTask task, CancellationToken ct)
    {
        var siteId = task.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var fileIo = FileIoObservationScope.Enter(services.GetService<IFileIoObservationProvider>(),
            new("downloadTask", siteId), "site:" + siteId);
        Volatile.Write(ref _transferProgress, null);
        Action<long, long?>? reportSpeed = null;
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(task.Options ?? "", JsonSerializerOptions.Web)
            ?? throw new ArgumentException("The cloud download request is missing.");
        request = CloudDownloadDestinations.ExecutionRequest(request);
        var providerId = CloudSites.Provider(task.ThirdPartyId);
        if (request.Source.ProviderId != providerId || Path.GetFullPath(request.OutputDirectory) != Path.GetFullPath(task.DownloadPath))
            throw new ArgumentException("The cloud task source or destination changed.");
        EnsureUserPathAllowed(task.DownloadPath);
        request = GuardRetainedSource(request, path => EnsureUserPathAllowed(path));
        var provider = DownloadIntents.DownloadProviderRouting.Require(GetRequiredService<IEnumerable<ICloudDownloadProvider>>(), request.Source);
        if (!task.IsPreparedPayloadHandoffOnly())
        {
            var readiness = await provider.CheckReadyAsync(request.AccountId, ct);
            if (!readiness.Available) throw new InvalidOperationException(readiness.Problem);
        }
        // Integrity failures require a manual start. The stored reason survives queue admission;
        // only providers that can identify the original failed member may change its checkpoint.
        Func<CloudDownloadRequest, Func<CloudDownloadProgress, Task>, CancellationToken, Task<CloudDownloadResult>> download =
            provider is ICloudDownloadChecksumRetryProvider checksumRetry &&
            CloudDownloadIntegrityFailures.IsChecksumMismatch(task.Message)
            ? (prepared, progress, token) => checksumRetry.RetryChecksumMismatchAsync(
                prepared, task.DownloadStatusUpdateDt, progress, token)
            : provider.DownloadAsync;
        var ownedCopies = provider is ICloudOwnedCopyProvider && CloudDownloadOwnedCopyService.IsShare(request)
            ? GetRequiredService<CloudDownloadOwnedCopyService>() : null;
        async Task<CloudDownloadResult> Download(CloudDownloadRequest prepared, CancellationToken token)
        {
            if (task.HasPreparedRetry())
            {
                if (provider is not ICloudPreparedDownloadRetryProvider retry || !task.TryConsumePreparedRetry(prepared.OperationId))
                    throw new InvalidOperationException("RECOVERY_DOWNLOAD_REVIEW_REQUIRED");
                // The saved copy is already pinned. Never prepare, restore or adopt it as
                // owned here; normal result registration below may finish an existing receipt.
                return task.IsPreparedPayloadHandoffOnly()
                    ? await retry.HandoffCompletedPreparedDownloadAsync(prepared, ReportProgress, token)
                    : await retry.RetryPreparedDownloadAsync(prepared, ReportProgress, token);
            }
            if (ownedCopies != null)
                await ownedCopies.PrepareAsync(task.Id, prepared, (ICloudOwnedCopyProvider)provider, token);
            return await download(prepared, ReportProgress, token);
        }
        async Task ReportProgress(CloudDownloadProgress p)
        {
            var snapshot = TransferSnapshot(p, DateTime.UtcNow);
            Volatile.Write(ref _transferProgress, snapshot);
            if (snapshot?.CompletedBytes is { } done)
                (reportSpeed ??= CreateMediaTransferSpeedReporter())(done, snapshot.TotalBytes);
            else if (reportSpeed != null)
            {
                ClearMediaDownloadSpeed();
                reportSpeed = null;
            }
            Current = p.Stage;
            await OnCurrentChangedInternal();
            if (p.Percentage is { } percent) await OnProgressInternal((decimal)Math.Clamp(percent, 0, 100));
        }
        async Task WaitingForCapacity()
        {
            Current = "WaitingForStorageCapacity";
            await OnCurrentChangedInternal();
        }
        Task<CloudDownloadResult> Run() => RunWithStorageAsync(services.GetService<IStorageWriteGuard>(), provider,
            request, Download, WaitingForCapacity, ct, () => ReportProgress(new(null, Stage: "Verifying")));
        var result = await RunWithCloudCapacityAsync(provider as ICloudDownloadCapacityWaitProvider, request, Run,
            () => ReportProgress(new(null, Stage: ICloudDownloadCapacityWaitProvider.WaitStage)), ct);
        CloudDownloadManifest.Validate(task.DownloadPath, result.Files);
        foreach (var file in result.Files)
        {
            EnsureUserPathAllowed(file);
            await OnFileDownloadedInternal(file);
        }
        if (result.Files.Count > 0)
        {
            // Provider completion precedes our immutable local-files receipt. Hashing one
            // large NAS file can outlast the idle watchdog; only successful reads refresh it.
            Volatile.Write(ref _transferProgress, null);
            ClearMediaDownloadSpeed();
            Current = "Verifying";
            await OnCurrentChangedInternal();
            var receipt = await GetRequiredService<DownloadResultService>().RecordFilesAsync(task.Id, task.ThirdPartyId,
                task.Key, task.Name ?? task.Key,
                CloudDownloadPayloadRoot.Select(task.DownloadPath, result.PayloadRootDirectory, result.Files),
                result.Files.ToArray(), null, _ => Touch(), ct);
            if (ownedCopies != null) await ownedCopies.DownloadVerifiedAsync(task.Id, request, receipt);
        }
        await OnProgressInternal(100);
    }

    internal static async Task<CloudDownloadResult> RunWithCloudCapacityAsync(ICloudDownloadCapacityWaitProvider? provider,
        CloudDownloadRequest request, Func<Task<CloudDownloadResult>> execute, Func<Task> waiting,
        CancellationToken ct, TimeSpan? checkInterval = null)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return await execute(); }
            catch (CloudProviderException error) when (error.CanWaitForCapacity && provider != null)
            {
                // The original attempt has released its local reservations, account lease and transfer lock.
                // Keep this native queue slot and its stop token. Poll metadata only until it fits.
                CloudReadiness readiness;
                do
                {
                    await waiting();
                    await Task.Delay(checkInterval ?? TimeSpan.FromMinutes(1), ct);
                    readiness = await provider.CheckWaitingCapacityAsync(request, ct);
                } while (!readiness.Available && readiness.CanWaitForCapacity);
                if (!readiness.Available) throw new CloudProviderException(readiness.Problem ?? error.Message);
                // The ordinary provider start validates the source, journal and capacity again under its write lock.
            }
        }
    }

    internal static async Task<CloudDownloadResult> RunWithStorageAsync(IStorageWriteGuard? writes,
        ICloudDownloadProvider provider, CloudDownloadRequest request,
        Func<CloudDownloadRequest, CancellationToken, Task<CloudDownloadResult>> download,
        Func<Task> waiting, CancellationToken ct, Func<Task>? verificationActivity = null)
    {
        var destinations = new[] { request.OutputDirectory, request.WorkspaceDirectory }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFullPath(p!)).ToArray();
        var context = new StorageWriteContext("cloudOperation", request.OperationId);
        if (writes is IStorageWriteReservationGuard reservations && provider is ICloudDownloadWriteBudgetProvider estimator
            && await estimator.PrepareWriteAsync(request, ct) is { } plan)
        {
            var started = false;
            Task<CloudDownloadResult> Run(CloudDownloadWritePlan attempt)
            {
                var allocations = attempt.Budgets.Select(b => new StorageWriteBudget(b.Destination, b.MaximumAdditionalBytes)).ToList();
                // Preserve monitoring of every declared placement even when it needs no payload budget
                // (for example, an unused workspace preference). Actual writes must all be in the estimate.
                var paths = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                var budgetPaths = allocations.Select(b => Path.GetFullPath(b.Destination)).ToHashSet(paths);
                allocations.AddRange(destinations.Where(p => !budgetPaths.Contains(p)).Select(p => new StorageWriteBudget(p, 0)));
                return reservations.RunReservedAsync(allocations, context, token =>
                {
                    started = true;
                    return download(attempt.Request, token);
                }, waiting, ct);
            }

            ValidatePreparedRequest(request, plan.Request);
            try { return await Run(plan); }
            catch (LocalStorageException error) when (!started && error.Code == StorageWriteGuard.ReserveLowCode)
            {
                var retained = await estimator.TryPrepareRetainedWriteAsync(
                    plan.Request, verificationActivity ?? (() => Task.CompletedTask), ct);
                if (retained == null) throw;
                ValidatePreparedRequest(plan.Request, retained.Request);
                return await Run(retained);
            }
        }
        // An absent estimate keeps the established monitor. It never claims reserved capacity.
        return writes == null ? await download(request, ct) :
            await writes.RunAsync(destinations, context, token => download(request, token), ct);
    }

    internal static CloudDownloadRequest GuardRetainedSource(CloudDownloadRequest request, Action<string> ensureAllowed)
    {
        if (request.ReuseSource is not { } source) return request;
        try { ensureAllowed(source.OutputDirectory); return request; }
        catch (UserStoragePathException)
        {
            // The selected source may have been disconnected since a successful staging pass.
            // Keep its identity for the destination receipt, but explicitly forbid any more reads from it.
            return request with {ReuseSource = source with {DisableSourceRead = true}};
        }
    }

    private static void ValidatePreparedRequest(CloudDownloadRequest original, CloudDownloadRequest prepared)
    {
        // A read-only preflight may resolve an account and pin its remote manifest, but cannot change
        // the caller's operation, source, placement, revisions or an already pinned remote identity.
        var originalFixed = original with {AccountId = null, RemoteEntry = null};
        var preparedFixed = prepared with {AccountId = null, RemoteEntry = null};
        var remote = prepared.RemoteEntry;
        if (original.ProviderLayoutId != prepared.ProviderLayoutId || original.ReuseSource != prepared.ReuseSource ||
            JsonSerializer.Serialize(originalFixed, JsonSerializerOptions.Web) != JsonSerializer.Serialize(preparedFixed, JsonSerializerOptions.Web) ||
            original.AccountId != null && original.AccountId != prepared.AccountId ||
            remote != null && (remote.ProviderId != original.Source.ProviderId || remote.AccountId != prepared.AccountId) ||
            original.RemoteEntry is { } pinned && (remote == null ||
                pinned.ProviderId != remote.ProviderId || pinned.AccountId != remote.AccountId || pinned.Path != remote.Path ||
                pinned.OwnedContainer != remote.OwnedContainer || pinned.ManifestJson != null && pinned.ManifestJson != remote.ManifestJson) ||
            original.RemoteEntry == null && original.Source.Kind == "remote" && remote != null && remote.Path != original.Source.Url)
            throw new CloudProviderException("CLOUD_DOWNLOAD_PREPARATION_IDENTITY_CHANGED");
    }
}
public sealed class BaiduCloudDownloader(IServiceProvider services) : CloudSiteDownloader<BaiduCloudTaskType>(services)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.BaiduNetdisk;
    public override BaiduCloudTaskType EnumTaskType => BaiduCloudTaskType.RemoteFileOrDirectory;
}
public sealed class GoogleCloudDownloader(IServiceProvider services) : CloudSiteDownloader<GoogleCloudTaskType>(services)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.GoogleDrive;
    public override GoogleCloudTaskType EnumTaskType => GoogleCloudTaskType.Share;
}
public sealed class GofileCloudDownloader(IServiceProvider services) : CloudSiteDownloader<GofileCloudTaskType>(services)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.Gofile;
    public override GofileCloudTaskType EnumTaskType => GofileCloudTaskType.Share;
}
public sealed class MegaCloudDownloader(IServiceProvider services) : CloudSiteDownloader<MegaCloudTaskType>(services)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.Mega;
    public override MegaCloudTaskType EnumTaskType => MegaCloudTaskType.Share;
}
public sealed class PikPakCloudDownloader(IServiceProvider services) : CloudSiteDownloader<PikPakCloudTaskType>(services)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.PikPak;
    public override PikPakCloudTaskType EnumTaskType => PikPakCloudTaskType.Share;
}
public sealed class OneDriveCloudDownloader(IServiceProvider services) : CloudSiteDownloader<OneDriveCloudTaskType>(services)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.OneDrive;
    public override OneDriveCloudTaskType EnumTaskType => OneDriveCloudTaskType.Share;
}
public sealed class PixeldrainCloudDownloader(IServiceProvider services) : CloudSiteDownloader<PixeldrainCloudTaskType>(services)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.Pixeldrain;
    public override PixeldrainCloudTaskType EnumTaskType => PixeldrainCloudTaskType.Share;
}
public sealed class TeraBoxCloudDownloader(IServiceProvider services) : CloudSiteDownloader<TeraBoxCloudTaskType>(services)
{
    public override ThirdPartyId ThirdPartyId => ThirdPartyId.TeraBox;
    public override TeraBoxCloudTaskType EnumTaskType => TeraBoxCloudTaskType.Share;
}

public sealed class CloudDownloaderHelper(IUserStoragePolicy storage, IBOptions<DownloaderGlobalOptions> global,
    IEnumerable<ICloudDownloadProvider> providers) : IDownloaderHelper
{
    public Task<DownloaderOptions> GetOptionsAsync() => Task.FromResult(new DownloaderOptions
        {DefaultPath = global.Value.DefaultDownloadPath, EffectiveDefaultPath = global.Value.DefaultDownloadPath});
    public Task PutOptionsAsync(DownloaderOptions options) => throw new InvalidOperationException("Configure cloud accounts in the cloud source settings.");
    public Task<BaseResponse> ValidateOptionsAsync() => Task.FromResult(new BaseResponse());
    public async Task<DownloadTask[]> BuildTasks(DownloadTaskAddInputModel model)
    {
        var providerId = CloudSites.Provider(model.ThirdPartyId);
        if (model.Type != 1 || model.Keys.Count != 1) throw new ArgumentException("Create one cloud source task at a time.");
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(model.Options ?? "", JsonSerializerOptions.Web)
            ?? throw new ArgumentException("Create cloud downloads from the cloud source form.");
        if (request.Source.ProviderId != providerId || (request.Source.Kind != "remote" &&
            CloudSourceClassifier.Classify(request.Source.Url) != providerId))
            throw new ArgumentException("The URL does not belong to the selected provider.");
        var output = DownloadDirectoryResolver.Resolve(model.DownloadPath, null, global.Value.DefaultDownloadPath, model.ThirdPartyId)
            ?? throw new ArgumentException("Choose a download directory.");
        storage.EnsurePathAllowed(output);
        var normalized = DownloadIntents.DownloadSourceResolver.Normalize(new()
        {
            Id = request.Source.Id, Url = request.Source.Url, Kind = request.Source.Kind, ProviderId = request.Source.ProviderId,
            MethodId = request.Source.MethodId, AccountId = request.Source.AccountId, AccessCode = request.Source.AccessCode
        });
        var descriptor = DownloadIntents.DownloadMethodCatalog.Select(normalized);
        if (descriptor?.ValidateSource?.Invoke(normalized) is { } sourceIssue) throw new ArgumentException(sourceIssue.Message);
        if (descriptor?.Availability is { } limitation) throw new InvalidOperationException(limitation.Description);
        var ready = await DownloadIntents.DownloadProviderRouting.Require(providers, normalized).CheckReadyAsync(request.AccountId, default);
        if (!ready.Available) throw new InvalidOperationException(ready.Problem);
        return [new DownloadTask
        {
            Key = model.Keys[0], Name = model.Names?.FirstOrDefault(), ThirdPartyId = model.ThirdPartyId,
            Type = 1, DownloadPath = output, Options = JsonSerializer.Serialize(request with {OutputDirectory = output}, JsonSerializerOptions.Web),
            AutoRetry = false, Status = DownloadTaskStatus.Idle
        }];
    }
}
