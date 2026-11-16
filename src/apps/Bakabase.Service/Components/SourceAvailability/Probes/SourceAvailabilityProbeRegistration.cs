using System;
using System.Threading;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

public static class SourceAvailabilityProbeRegistration
{
    public static IServiceCollection AddSourceAvailabilityProbes(this IServiceCollection services)
    {
        services.AddHttpClient(GoFileAvailabilityProbe.ClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(GoFileWebsiteToken.UserAgent);
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(sp => GoFileMetadataHttp.Create(
            sp.GetRequiredService<BakabaseWebProxy>().ForDownloadTool(DownloadToolProxyTargets.GalleryDl, ThirdPartyId.Gofile)));
        services.AddSingleton<GoFileAnonymousSession>();
        services.AddSingleton<IGoFileWebsiteToken, GoFileWebsiteToken>();
        services.AddHttpClient(PixeldrainAvailabilityProbe.ClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Bakabase-SourceAvailability/1.0");
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(sp => new ProxyAwarePublicHttpHandler(
            sp.GetRequiredService<BakabaseWebProxy>().ForDownloadTool(DownloadToolProxyTargets.GalleryDl, ThirdPartyId.Pixeldrain),
            uri => uri.Scheme == "https" && uri.IdnHost == "pixeldrain.com" && uri.IsDefaultPort &&
                   uri.UserInfo.Length == 0 && uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal), followRedirects: false));
        services.AddHttpClient(HttpSourceAvailabilityProbe.ClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Bakabase-SourceAvailability/1.0");
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(sp => new ProxyAwarePublicHttpHandler(sp.GetRequiredService<BakabaseWebProxy>()));
        services.AddSingleton<ISourceAvailabilityProbe, BaiduShareAvailabilityProbe>();
        services.AddSingleton<ISourceAvailabilityProbe, GoFileAvailabilityProbe>();
        services.AddSingleton<PixeldrainAvailabilityProbe>();
        services.AddSingleton<ISourceAvailabilityProbe>(sp => sp.GetRequiredService<PixeldrainAvailabilityProbe>());
        services.AddSingleton<ISourceAvailabilityProbe, SharePointAvailabilityProbe>();
        services.AddSingleton<ISourceAvailabilityProbe, CloudDownloads.TeraBox.TeraBoxSourceAvailabilityProbe>();
        services.AddSingleton<ISourceAvailabilityProbe, HttpSourceAvailabilityProbe>();
        // This fallback must be last; the shared service resolves the first matching probe.
        services.AddSingleton<ISourceAvailabilityProbe, UnsupportedSourceAvailabilityProbe>();
        return services;
    }
}
