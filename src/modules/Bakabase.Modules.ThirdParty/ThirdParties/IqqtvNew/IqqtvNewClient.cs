using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.IqqtvNew.Models;
using Microsoft.Extensions.Logging;
using Bakabase.Modules.ThirdParty.ThirdParties.Iqqtv;

namespace Bakabase.Modules.ThirdParty.ThirdParties.IqqtvNew;

public class IqqtvNewClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<IqqtvNewVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        // Delegate to Iqqtv with JP language as the python script does
        var delegated = new IqqtvClient(httpClientFactory, loggerFactory);
        var adjustedUrl = appointUrl?.Replace("/cn/", "/jp/");
        var result = await delegated.SearchAndParseVideo(number, adjustedUrl, "jp");
        if (result == null) return null;
        return new IqqtvNewVideoDetail
        {
            Number = result.Number,
            Title = result.Title,
            OriginalTitle = result.OriginalTitle,
            Actor = result.Actor,
            Tag = result.Tag,
            Release = result.Release,
            Year = result.Year,
            Studio = result.Studio,
            Publisher = result.Publisher,
            Series = result.Series,
            Runtime = result.Runtime,
            Source = result.Source,
            CoverUrl = result.CoverUrl,
            PosterUrl = result.PosterUrl,
            Website = result.Website,
            Mosaic = result.Mosaic,
            SearchUrl = result.SearchUrl
        };
    }
}


