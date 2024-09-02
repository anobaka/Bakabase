using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Abstractions.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBakabaseComponents(this IServiceCollection services)
    {
        services.TryAddSingleton<IFileManager, FileManager>();
        return services;
    }

    public static IServiceCollection AddBakabaseHttpClient<THttpClientHandler>(this IServiceCollection services,
        string name) where THttpClientHandler : HttpClientHandler
    {
        services.TryAddSingleton<THttpClientHandler>();
        services.AddHttpClient(name,
                t => { t.DefaultRequestHeaders.Add("User-Agent", InternalOptions.DefaultHttpUserAgent); })
            // todo: let http client factory handle its lifetime automatically after changing queue mechanism of inside world handler 
            .SetHandlerLifetime(TimeSpan.FromDays(30))
            .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<THttpClientHandler>());

        return services;
    }
}