using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.CloudDownloads.Rclone;

namespace Bakabase.Service.Components.CloudDownloads;

public static class CloudDownloadRegistration
{
    public static IServiceCollection AddCloudDownloads(this IServiceCollection services)
    {
        services.AddSingleton<IStorageWritePolicyProvider,
            Bakabase.Service.Components.Acquisition.Planning.AcquisitionStorageWritePolicyProvider>();
        services.AddSingleton<IStorageWriteGuard, StorageWriteGuard>();
        services.AddSingleton<IFileArtifactMaterializer, FileArtifactMaterializer>();
        services.AddSingleton<BaiduPcsBridgeClient>();
        services.AddSingleton<BaiduAccountSnapshot>();
        services.AddSingleton<BaiduPcsAccountStore>();
        services.AddSingleton<Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components.IDownloadTaskConcurrencyPolicy,
            BaiduDownloadTaskConcurrencyPolicy>();
        services.AddSingleton<ICloudDownloadProvider, BaiduCloudDownloadProvider>();
        services.AddSingleton<ICloudDownloadProvider, GoogleDriveCloudProvider>();
        services.AddSingleton<ICloudDownloadProvider, GofileCloudProvider>();
        services.AddSingleton<ICloudDownloadProvider, PixeldrainCloudProvider>();
        services.AddSingleton<ICloudDownloadProvider, Mega.MegaCmdCloudProvider>();
        services.AddSingleton<PikPakCli.PikPakCliAccountService>();
        services.AddSingleton<ICloudDownloadProvider, PikPakCli.PikPakCliCloudProvider>();
        services.AddSingleton<TeraBox.TeraBoxAccountService>();
        services.AddSingleton<ICloudDownloadAccountResolver>(sp => sp.GetRequiredService<TeraBox.TeraBoxAccountService>());
        services.AddSingleton<TeraBox.TeraBoxCloudProvider>();
        services.AddSingleton<ICloudDownloadProvider>(sp => sp.GetRequiredService<TeraBox.TeraBoxCloudProvider>());
        services.AddSingleton<ICloudDownloadProvider, SharePointCloudProvider>();
        services.AddPublicShareDownloadClients();
        services.AddHttpClient(Bakabase.Service.Components.PostSources.PostSourceQr.Client, c =>
        {
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 Bakabase-SavedPostImage/1.0");
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(sp => new ProxyAwarePublicHttpHandler(
            sp.GetRequiredService<Bakabase.Abstractions.Components.Network.BakabaseWebProxy>(),
            checkProxyTarget: Bakabase.Service.Components.PostSources.SavedPostImages.CheckProxyTargetAsync));
        services.AddSingleton<RcloneRemoteEngine>();
        services.AddSingleton<RcloneDownloadSnapshotStore>();
        services.AddRcloneAccounts();
        services.AddSingleton<ICloudDownloadProvider, RclonePikPakCloudProvider>();
        services.AddSingleton<ICloudDownloadProvider, RcloneOneDriveCloudProvider>();
        services.AddSingleton<ICloudDownloadProvider, RclonePixeldrainAccountCloudProvider>();
        services.AddSingleton<ICloudDownloadProvider, PixeldrainRcloneCloudProvider>();
        services.AddSingleton<IDownloadSourceResolver, HttpRedirectSourceResolver>();
        services.AddSingleton<DownloadSourceResolver>();
        Bakabase.Service.Components.SourceAvailability.Probes.SourceAvailabilityProbeRegistration.AddSourceAvailabilityProbes(services);
        services.AddSingleton<Bakabase.Service.Components.SourceAvailability.SourceAvailabilityStore>();
        services.AddSingleton<Bakabase.Service.Components.SourceAvailability.SourceAvailabilityService>();
        services.AddScoped<Bakabase.Service.Components.SourceAvailability.SourceAvailabilityBatchService>();
        services.AddScoped<Bakabase.Modules.PostParser.Services.IPostLinkHealthChecker, Bakabase.Service.Components.SourceAvailability.SharedPostLinkHealthChecker>();
        services.AddScoped<CloudOperationService>();
        services.AddScoped<CloudDownloadRecoveryService>();
        services.AddScoped<ICloudDownloadDestinationService, CloudDownloadDestinationService>();
        services.AddScoped<DownloadMethodRegistry>();
        services.AddScoped<DownloadIntentStarter>();
        services.AddScoped<DownloadIntentService>();
        services.AddScoped<PostDownloadIntentAdapter>();
        services.AddScoped<PostAcquisitionPlanAdapter>();
        services.AddScoped<PostLocalProcessingService>();
        services.AddSingleton<Bakabase.Service.Controllers.IWorkflowSourceRunProjection, CloudSourceRunProjection>();
        services.AddScoped<IArtifactOperationStore, ArtifactOperationStore>();
        services.AddScoped<DownloadArtifactCoordinator>();
        services.AddScoped<CloudDownloadDirectoryProvenanceResolver>();
        services.AddSingleton(new CloudDownloadDirectoryProvenanceRegistration("baidu", null, sp =>
            new BaiduDownloadDirectoryProvenanceProvider(sp.GetRequiredService<BaiduPcsAccountStore>())));
        services.AddScoped<FileProcessingPlanExecutor>();
        services.AddScoped<RecursiveArchiveExecutor>();
        services.AddScoped<FileProcessingWorkspaceResolver>(sp => new(
            sp.GetRequiredService<AppService>().AppDataDirectory, sp.GetRequiredService<IUserStoragePolicy>()));
        services.AddScoped<FileArtifactProcessor>();
        services.AddScoped<ArtifactWorkspaceMaintenance>();
        services.AddScoped<PublishedArtifactAncestorCleanup>();
        services.AddScoped<IWorkflowRunCompletionObserver, PublishedArtifactCleanupCompletionObserver>();
        services.AddHostedService<ArtifactWorkspaceLifecycleCleanup>();
        services.AddScoped<FileArtifactPublisher>();
        services.AddScoped<ArchiveQuarantineService>();
        services.AddHostedService<ArchiveQuarantineEmptyDirectoryCleanup>();
        services.AddSingleton<IWorkflowTrigger, CloudSourcesTrigger>();
        services.AddSingleton<IWorkflowItemTypeDescriptor, CloudWorksetDescriptor>();
        services.AddSingleton<IWorkflowActivity, PrepareCloudSourcesActivity>();
        services.AddSingleton<IWorkflowActivity, SelectCloudSourcesActivity>();
        services.AddSingleton<IWorkflowActivity, BaiduTransferActivity>();
        services.AddSingleton<IWorkflowActivity, BaiduDownloadActivity>();
        services.AddSingleton<IWorkflowActivity, EnqueueCloudSourcesActivity>();
        services.AddSingleton<IWorkflowActivity, AwaitCloudDownloadsActivity>();
        services.AddSingleton<IWorkflowActivity, ProcessCloudFilesActivity>();
        services.AddSingleton<IWorkflowActivity, CleanupDownloadArtifactsActivity>();
        services.AddSingleton<IWorkflowActivity, ProcessLocalFileArtifactsActivity>();
        services.AddSingleton<IWorkflowActivity, PublishFileArtifactsActivity>();
        services.AddSingleton<IWorkflowActivity, AssessFileArtifactDeliveryActivity>();
        services.AddSingleton<IWorkflowActivity, FetchSupportedSourcesActivity>();
        return services;
    }
}
