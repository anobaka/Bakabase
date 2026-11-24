using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Downloader.Extensions;

public static class DownloaderServiceCollectionExtensions
{
    internal sealed record AuthorizedClient(string Name, int MaxParallelRanges, long MinimumParallelBytes);

    /// <summary>Opt a provider's HTTPS/CDN-restricted client into signed original reads, without HTTP factory loggers.</summary>
    public static IHttpClientBuilder ForAuthorizedOriginalDownloads(this IHttpClientBuilder builder,
        int maxParallelRanges = 1, long minimumParallelBytes = 0)
    {
        if (maxParallelRanges is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(maxParallelRanges));
        ArgumentOutOfRangeException.ThrowIfNegative(minimumParallelBytes);
        builder.RemoveAllLoggers();
        builder.Services.AddSingleton(new AuthorizedClient(builder.Name, maxParallelRanges, minimumParallelBytes));
        return builder;
    }

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
            sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILoggerFactory>(), sp.GetService<IStorageWriteGuard>()));
        services.TryAddSingleton<IAuthorizedOriginalHttpDownloader, AuthorizedOriginalHttpDownloader>();
        services.TryAddSingleton<ITorrentDownloader>(sp => new BuiltInTorrentDownloader(
            () => cacheDirectory(sp), sp.GetRequiredService<ILogger<BuiltInTorrentDownloader>>(),
            sp.GetRequiredService<IHttpClientFactory>(), sp.GetService<ITorrentNetworkOptionsProvider>(), sp.GetService<IStorageWriteGuard>()));
        services.TryAddSingleton<IAria2Downloader, Aria2Downloader>();
        return services;
    }
}
