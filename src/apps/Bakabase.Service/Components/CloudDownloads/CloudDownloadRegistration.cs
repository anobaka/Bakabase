using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.CloudDownloads;

public static class CloudDownloadRegistration
{
    public static IServiceCollection AddCloudDownloads(this IServiceCollection services)
    {
        services.AddSingleton<BaiduPcsBridgeClient>();
        services.AddSingleton<BaiduPcsAccountStore>();
        services.AddSingleton<ICloudDownloadProvider, BaiduCloudDownloadProvider>();
        services.AddSingleton<ICloudDownloadProvider, GoogleDriveCloudProvider>();
        services.AddSingleton<ICloudDownloadProvider, GofileCloudProvider>();
        services.AddScoped<CloudOperationService>();
        services.AddSingleton<IWorkflowTrigger, CloudSourcesTrigger>();
        services.AddSingleton<IWorkflowItemTypeDescriptor, CloudWorksetDescriptor>();
        services.AddSingleton<IWorkflowActivity, PrepareCloudSourcesActivity>();
        services.AddSingleton<IWorkflowActivity, SelectCloudSourcesActivity>();
        services.AddSingleton<IWorkflowActivity, BaiduTransferActivity>();
        services.AddSingleton<IWorkflowActivity, BaiduDownloadActivity>();
        services.AddSingleton<IWorkflowActivity, EnqueueCloudSourcesActivity>();
        services.AddSingleton<IWorkflowActivity, AwaitCloudDownloadsActivity>();
        return services;
    }
}
