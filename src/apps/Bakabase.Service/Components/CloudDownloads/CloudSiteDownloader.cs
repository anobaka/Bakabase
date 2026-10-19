using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
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

public static class CloudSites
{
    public static ThirdPartyId Site(string provider) => provider switch
    {
        "baidu" => ThirdPartyId.BaiduNetdisk, "googleDrive" => ThirdPartyId.GoogleDrive,
        "gofile" => ThirdPartyId.Gofile, _ => throw new ArgumentException("This source has no installed download adapter.")
    };
    public static string Provider(ThirdPartyId site) => site switch
    {
        ThirdPartyId.BaiduNetdisk => "baidu", ThirdPartyId.GoogleDrive => "googleDrive",
        ThirdPartyId.Gofile => "gofile", _ => throw new ArgumentException("Choose a cloud download source.")
    };
}

/// <summary>Cloud adapters use the established site queue, lifecycle, file accounting and download center.</summary>
public abstract class CloudSiteDownloader<T>(IServiceProvider services) : AbstractDownloader<T>(services) where T : struct
{
    protected override async Task StartCore(DownloadTask task, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<CloudDownloadRequest>(task.Options ?? "", JsonSerializerOptions.Web)
            ?? throw new ArgumentException("The cloud download request is missing.");
        var providerId = CloudSites.Provider(task.ThirdPartyId);
        if (request.Source.ProviderId != providerId || Path.GetFullPath(request.OutputDirectory) != Path.GetFullPath(task.DownloadPath))
            throw new ArgumentException("The cloud task source or destination changed.");
        EnsureUserPathAllowed(task.DownloadPath);
        var provider = GetRequiredService<IEnumerable<ICloudDownloadProvider>>().Single(p => p.ProviderId == providerId);
        var readiness = await provider.CheckReadyAsync(request.AccountId, ct);
        if (!readiness.Available) throw new InvalidOperationException(readiness.Problem);
        var result = await provider.DownloadAsync(request, async p =>
        {
            Current = p.Stage;
            await OnCurrentChangedInternal();
            if (p.Percentage is { } percent) await OnProgressInternal((decimal)Math.Clamp(percent, 0, 100));
        }, ct);
        CloudDownloadManifest.Validate(task.DownloadPath, result.Files);
        foreach (var file in result.Files)
        {
            EnsureUserPathAllowed(file);
            await OnFileDownloadedInternal(file);
        }
        if (result.Files.Count > 0)
            await GetRequiredService<DownloadResultService>().RecordFilesAsync(task.Id, task.ThirdPartyId,
                task.Key, task.Name ?? task.Key, task.DownloadPath, result.Files.ToArray(), null, ct);
        await OnProgressInternal(100);
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
        var ready = await providers.Single(p => p.ProviderId == providerId).CheckReadyAsync(request.AccountId, default);
        if (!ready.Available) throw new InvalidOperationException(ready.Problem);
        return [new DownloadTask
        {
            Key = model.Keys[0], Name = model.Names?.FirstOrDefault(), ThirdPartyId = model.ThirdPartyId,
            Type = 1, DownloadPath = output, Options = JsonSerializer.Serialize(request with {OutputDirectory = output}, JsonSerializerOptions.Web),
            AutoRetry = false, Status = DownloadTaskStatus.Idle
        }];
    }
}
