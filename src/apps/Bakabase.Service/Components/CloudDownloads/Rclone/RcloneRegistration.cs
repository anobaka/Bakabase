using Microsoft.Extensions.DependencyInjection;
using Bakabase.Service.Components.OAuth;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

public static class RcloneRegistration
{
    public static IServiceCollection AddRcloneAccounts(this IServiceCollection services)
    {
        services.AddOAuthAuthorization();
        services.AddSingleton<RcloneAccountStore>();
        services.AddSingleton<RcloneBrowserAuthorizationProcess>();
        services.AddSingleton<RcloneDriveDiscovery>();
        services.AddSingleton<RcloneAuthorizationService>();
        services.AddHostedService<RcloneAuthorizationCleanup>();
        return services;
    }
}
