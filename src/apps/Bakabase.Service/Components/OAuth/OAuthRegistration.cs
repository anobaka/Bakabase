using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service.Components.OAuth;

public static class OAuthRegistration
{
    public static IServiceCollection AddOAuthAuthorization(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOAuthProvider, MicrosoftGraphOAuthProvider>());
        services.TryAddSingleton<OAuthProviderRegistry>();
        services.TryAddSingleton<OAuthClientConfigurationStore>();
        services.TryAddSingleton<OAuthAuthorizationBroker>();
        services.AddHostedService<OAuthAuthorizationCleanup>();
        return services;
    }
}

internal sealed class OAuthAuthorizationCleanup(OAuthAuthorizationBroker broker) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            try { await broker.CleanupExpiredAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { /* No authorization/request data is logged from maintenance. */ }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        } while (!stoppingToken.IsCancellationRequested);
    }
}
