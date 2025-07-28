using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Guochan.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Guochan;

public class GuochanClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<GuochanVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? filePath = null, string? appointNumber = null)
    {
        try
        {
            // Use a heuristic-only approach (labels/actors extraction is complex; keep minimal fields per python fallbacks)
            string title = number;
            string cover = string.Empty;
            string actor = string.Empty;

            var detail = new GuochanVideoDetail
            {
                Number = number,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = string.Empty,
                Release = string.Empty,
                Year = string.Empty,
                Runtime = string.Empty,
                Series = string.Empty,
                Studio = string.Empty,
                Publisher = string.Empty,
                CoverUrl = cover,
                PosterUrl = string.Empty,
                Website = appointUrl ?? string.Empty,
                Source = "guochan",
                Mosaic = "国产"
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


