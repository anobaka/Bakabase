using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Downloader.Extensions;

public static class DownloaderServiceCollectionExtensions
{
    public const string HttpClientName = "Bakabase.Downloader";

    /// <summary>
    /// Registers application-independent transfer services. The host owns the cache location;
    /// resolve it at transfer time because the active application data directory can change.
    /// </summary>
    public static IServiceCollection AddDownloader(this IServiceCollection services,
        Func<IServiceProvider, string> cacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(cacheDirectory);
        services.AddHttpClient(HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.TryAddSingleton<IHttpDownloader>(sp => new HttpDownloader(() => cacheDirectory(sp),
            sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILoggerFactory>()));
        services.TryAddSingleton<ITorrentDownloader>(sp => new BuiltInTorrentDownloader(
            () => cacheDirectory(sp), sp.GetRequiredService<ILogger<BuiltInTorrentDownloader>>(),
            sp.GetRequiredService<IHttpClientFactory>()));
        services.TryAddSingleton<IAria2Downloader, Aria2Downloader>();
        return services;
    }
}
