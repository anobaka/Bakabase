using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.DownloadIntents;

/// <summary>The selected method controls execution while ProviderId continues to identify the site.</summary>
public static class DownloadProviderRouting
{
    public static ICloudDownloadProvider? Find(IEnumerable<ICloudDownloadProvider> providers, CloudSource source)
    {
        var method = DownloadMethodCatalog.Select(source);
        if (method == null) return null;
        var candidates = providers.Where(p => p.ProviderId == source.ProviderId).ToList();
        return candidates.SingleOrDefault(p => p.MethodId == method?.Id) ??
            (method?.EngineId == "rclone" ? null : candidates.SingleOrDefault(p => p.MethodId == null));
    }

    public static ICloudDownloadProvider Require(IEnumerable<ICloudDownloadProvider> providers, CloudSource source) =>
        Find(providers, source) ?? throw new InvalidOperationException("The selected download method has no registered implementation.");
}
