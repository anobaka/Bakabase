using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;

public static class BilibiliCdnServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="InternalOptions.HttpClientNames.BilibiliCdn"/> client (see
    /// <see cref="BilibiliCdnHttpMessageHandler{TOptions}"/>) and <see cref="BilibiliCdnDownloader"/>. The client
    /// has no overall timeout: a stream can take hours; the downloader bounds headers and stalls itself.
    /// Settings, <see cref="TimeProvider"/> and the downloader are <c>TryAdd</c>ed, so a test can register its
    /// own <see cref="BilibiliCdnDownloaderSettings"/> first.
    /// </summary>
    public static IServiceCollection AddBilibiliCdn<TBilibiliOptions>(this IServiceCollection services)
        where TBilibiliOptions : class, IThirdPartyHttpClientOptions, new()
    {
        services.AddBakabaseHttpClient<BilibiliCdnHttpMessageHandler<TBilibiliOptions>>(
            InternalOptions.HttpClientNames.BilibiliCdn);
        services.AddHttpClient(InternalOptions.HttpClientNames.BilibiliCdn)
            .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new BilibiliCdnDownloaderSettings());
        services.TryAddSingleton<BilibiliCdnDownloader>();
        return services;
    }
}
