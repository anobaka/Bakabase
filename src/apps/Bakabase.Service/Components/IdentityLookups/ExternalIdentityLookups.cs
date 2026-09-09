using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.Steam;

namespace Bakabase.Service.Components.IdentityLookups;

/// <summary>
/// Asks DLsite what a work id refers to.
/// </summary>
public class DLsiteIdentityLookup(DLsiteClient client) : IExternalIdentityLookup
{
    public ResourceSource Source => ResourceSource.DLsite;

    public async Task<ExternalIdentityDetail?> Lookup(string sourceKey, CancellationToken ct)
    {
        var detail = await client.ParseWorkDetailById(sourceKey);
        return detail == null
            ? null
            : new ExternalIdentityDetail(sourceKey, detail.Name,
                detail.CoverUrls == null ? null : [..detail.CoverUrls]);
    }
}

public class SteamIdentityLookup(SteamClient client) : IExternalIdentityLookup
{
    public ResourceSource Source => ResourceSource.Steam;

    public async Task<ExternalIdentityDetail?> Lookup(string sourceKey, CancellationToken ct)
    {
        if (!int.TryParse(sourceKey, out var appId))
        {
            return null;
        }

        var details = await client.GetAppDetails(appId, ct: ct);
        return details == null
            ? null
            : new ExternalIdentityDetail(sourceKey, details.Name,
                string.IsNullOrEmpty(details.HeaderImage) ? null : [details.HeaderImage]);
    }
}

public class BangumiIdentityLookup(BangumiClient client) : IExternalIdentityLookup
{
    public ResourceSource Source => ResourceSource.Bangumi;

    public async Task<ExternalIdentityDetail?> Lookup(string sourceKey, CancellationToken ct)
    {
        var detail = await client.ParseDetail($"https://bgm.tv/subject/{sourceKey}");
        return detail == null
            ? null
            : new ExternalIdentityDetail(sourceKey, detail.Name,
                string.IsNullOrEmpty(detail.CoverUrl) ? null : [detail.CoverUrl]);
    }
}

public class ExHentaiIdentityLookup(ExHentaiClient client) : IExternalIdentityLookup
{
    public ResourceSource Source => ResourceSource.ExHentai;

    public async Task<ExternalIdentityDetail?> Lookup(string sourceKey, CancellationToken ct)
    {
        // A gallery is addressed by both its number and its token, which is how the source key is
        // stored; either half alone cannot be fetched.
        var parts = sourceKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        var gallery = await client.ParseDetail($"https://exhentai.org/g/{parts[0]}/{parts[1]}/", false);
        return gallery == null
            ? null
            : new ExternalIdentityDetail(sourceKey, gallery.Name,
                string.IsNullOrEmpty(gallery.CoverUrl) ? null : [gallery.CoverUrl]);
    }
}
