using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Components.Enhancers;
using Bakabase.Modules.Enhancer.Extensions;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.Airav;
using Bakabase.Modules.ThirdParty.ThirdParties.AiravCC;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsex;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsox;
using Bakabase.Modules.ThirdParty.ThirdParties.CableAV;
using Bakabase.Modules.ThirdParty.ThirdParties.CNMDB;
using Bakabase.Modules.ThirdParty.ThirdParties.DMM;
using Bakabase.Modules.ThirdParty.ThirdParties.Dahlia;
using Bakabase.Modules.ThirdParty.ThirdParties.Dmm;
using Bakabase.Modules.ThirdParty.ThirdParties.FC2;
using Bakabase.Modules.ThirdParty.ThirdParties.Faleno;
using Bakabase.Modules.ThirdParty.ThirdParties.Fantastica;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2club;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2hub;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2ppvdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Freejavbt;
using Bakabase.Modules.ThirdParty.ThirdParties.Getchu;
using Bakabase.Modules.ThirdParty.ThirdParties.GetchuDl;
using Bakabase.Modules.ThirdParty.ThirdParties.Giga;
using Bakabase.Modules.ThirdParty.ThirdParties.Guochan;
using Bakabase.Modules.ThirdParty.ThirdParties.Hdouban;
using Bakabase.Modules.ThirdParty.ThirdParties.Hscangku;
using Bakabase.Modules.ThirdParty.ThirdParties.Iqqtv;
using Bakabase.Modules.ThirdParty.ThirdParties.IqqtvNew;
using Bakabase.Modules.ThirdParty.ThirdParties.Jav321;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus;
using Bakabase.Modules.ThirdParty.ThirdParties.Javday;
using Bakabase.Modules.ThirdParty.ThirdParties.Javdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Javlibrary;
using Bakabase.Modules.ThirdParty.ThirdParties.Kin8;
using Bakabase.Modules.ThirdParty.ThirdParties.Love6;
using Bakabase.Modules.ThirdParty.ThirdParties.Lulubar;
using Bakabase.Modules.ThirdParty.ThirdParties.Madouqu;
using Bakabase.Modules.ThirdParty.ThirdParties.Mdtv;
using Bakabase.Modules.ThirdParty.ThirdParties.Mgstage;
using Bakabase.Modules.ThirdParty.ThirdParties.Mmtv;
using Bakabase.Modules.ThirdParty.ThirdParties.Mywife;
using Bakabase.Modules.ThirdParty.ThirdParties.Official;
using Bakabase.Modules.ThirdParty.ThirdParties.Prestige;
using Bakabase.Modules.ThirdParty.ThirdParties.ThePornDB;
using Bakabase.Modules.ThirdParty.ThirdParties.ThePornDBMovies;
using Bakabase.Modules.ThirdParty.ThirdParties.Xcity;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Av;

public class AvEnhancer(
    ILoggerFactory loggerFactory,
    IFileManager fileManager,
    AiravClient airavClient,
    AiravCCClient airavCCClient,
    AvsexClient avsexClient,
    AvsoxClient avsoxClient,
    CableAVClient cableAvClient,
    CNMDBClient cnmdbClient,
    DmmClient dmmClient,
    DahliaClient dahliaClient,
    FC2Client fc2Client,
    FalenoClient falenoClient,
    FantasticaClient fantasticaClient,
    Fc2clubClient fc2clubClient,
    Fc2hubClient fc2hubClient,
    Fc2ppvdbClient fc2ppvdbClient,
    FreejavbtClient freejavbtClient,
    GetchuClient getchuClient,
    GetchuDlClient getchuDlClient,
    GigaClient gigaClient,
    GuochanClient guochanClient,
    HdoubanClient hdoubanClient,
    HscangkuClient hscangkuClient,
    IqqtvClient iqqtvClient,
    IqqtvNewClient iqqtvNewClient,
    Jav321Client jav321Client,
    JavbusClient javbusClient,
    JavdayClient javdayClient,
    JavdbClient javdbClient,
    JavlibraryClient javlibraryClient,
    Kin8Client kin8Client,
    Love6Client love6Client,
    LulubarClient lulubarClient,
    MadouquClient madouquClient,
    MdtvClient mdtvClient,
    MgstageClient mgstageClient,
    MmtvClient mmtvClient,
    MywifeClient mywifeClient,
    OfficialClient officialClient,
    PrestigeClient prestigeClient,
    ThePornDBClient thePornDbClient,
    ThePornDBMoviesClient thePornDbMoviesClient,
    XcityClient xcityClient, IStandardValueService standardValueService, ISpecialTextService specialTextService, IServiceProvider serviceProvider)
    : AbstractKeywordEnhancer<AvEnhancerTarget, AvEnhancerContext, IKeywordEnhancerOptions>(loggerFactory, fileManager, standardValueService, specialTextService, serviceProvider)
{
    protected override EnhancerId TypedId => EnhancerId.Av;

    protected override async Task<AvEnhancerContext?> BuildContextInternal(string keyword, Resource resource, IKeywordEnhancerOptions options,
        EnhancementLogCollector logCollector, CancellationToken ct)
    {
        try
        {
            logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                $"Searching AV with keyword: {keyword} using 38 data sources",
                new { Keyword = keyword, SourceCount = 38, Sources = new[] { "airav", "airavcc", "avsex", "avsox", "cableav", "cnmdb", "dmm", "dahlia", "fc2", "faleno", "fantastica", "fc2club", "fc2hub", "fc2ppvdb", "freejavbt", "getchu", "getchudl", "giga", "guochan", "hdouban", "hscangku", "iqqtv", "iqqtvnew", "jav321", "javbus", "javday", "javdb", "javlibrary", "kin8", "love6", "lulubar", "madouqu", "mdtv", "mgstage", "mmtv", "mywife", "official", "prestige", "theporndb", "theporndbmovies", "xcity" } });

            var context = new AvEnhancerContext();

            // Define all clients that implement SearchAndParseVideo returning IAvDetail
            var clients = new Dictionary<string, Func<string, string?, Task<IAvDetail?>>>
            {
                { "airav", (num, url) => airavClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "airavcc", (num, url) => airavCCClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "avsex", (num, url) => avsexClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "avsox", (num, url) => avsoxClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "cableav", (num, url) => cableAvClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "cnmdb", (num, url) => cnmdbClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "dmm", (num, url) => dmmClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "dahlia", (num, url) => dahliaClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "fc2", (num, url) => fc2Client.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "faleno", (num, url) => falenoClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "fantastica", (num, url) => fantasticaClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "fc2club", (num, url) => fc2clubClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "fc2hub", (num, url) => fc2hubClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "fc2ppvdb", (num, url) => fc2ppvdbClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "freejavbt", (num, url) => freejavbtClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "getchu", (num, url) => getchuClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "getchudl", (num, url) => getchuDlClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "giga", (num, url) => gigaClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "guochan", (num, url) => guochanClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "hdouban", (num, url) => hdoubanClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "hscangku", (num, url) => hscangkuClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "iqqtv", (num, url) => iqqtvClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "iqqtvnew", (num, url) => iqqtvNewClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "jav321", (num, url) => jav321Client.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "javbus", (num, url) => javbusClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "javday", (num, url) => javdayClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "javdb", (num, url) => javdbClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "javlibrary", (num, url) => javlibraryClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "kin8", (num, url) => kin8Client.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "love6", (num, url) => love6Client.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "lulubar", (num, url) => lulubarClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "madouqu", (num, url) => madouquClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "mdtv", (num, url) => mdtvClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "mgstage", (num, url) => mgstageClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "mmtv", (num, url) => mmtvClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "mywife", (num, url) => mywifeClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "official", (num, url) => officialClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "prestige", (num, url) => prestigeClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "theporndb", (num, url) => thePornDbClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "theporndbmovies", (num, url) => thePornDbMoviesClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "xcity", (num, url) => xcityClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) }
            };

            // Try each client and collect results
            var successfulSources = new List<string>();
            var failedSources = new List<string>();
            var tasks = clients.Select(async kvp =>
            {
                try
                {
                    var result = await kvp.Value(keyword, null);
                    if (result != null)
                    {
                        lock (successfulSources)
                        {
                            successfulSources.Add(kvp.Key);
                        }
                        Logger.LogInformation($"Found result from {kvp.Key}: {result.Source}");
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    lock (failedSources)
                    {
                        failedSources.Add(kvp.Key);
                    }
                    Logger.LogDebug($"Client {kvp.Key} failed: {ex.Message}");
                }
                return null;
            });

            var results = await Task.WhenAll(tasks);
            context.Details = results.Where(r => r != null).ToList()!;

            logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
                $"Found {context.Details.Count} results from different sources",
                new {
                    ResultCount = context.Details.Count,
                    SuccessfulSources = successfulSources,
                    FailedSourceCount = failedSources.Count,
                    Sources = context.Details.Select(d => new { Source = d.Source, SearchUrl = d.SearchUrl }).ToList()
                });

            if (!context.Details.Any())
            {
                return null;
            }

            // Download only the first successful cover and poster images
            var coverSaved = false;
            var posterSaved = false;
            foreach (var detail in context.Details)
            {
                if (coverSaved && posterSaved)
                {
                    break;
                }

                try
                {
                    if (!coverSaved && !string.IsNullOrEmpty(detail.CoverUrl))
                    {
                        logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                            $"Downloading cover from {detail.Source}",
                            new { Url = detail.CoverUrl, Source = detail.Source });

                        var imageData = await airavClient.HttpClient.GetByteArrayAsync(detail.CoverUrl, ct);

                        logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
                            $"Cover downloaded from {detail.Source} ({imageData.Length} bytes)",
                            new { Url = detail.CoverUrl, Source = detail.Source, Size = imageData.Length });

                        var extension = Path.GetExtension(detail.CoverUrl.Split('?')[0]) ?? ".jpg";
                        var coverPath = await SaveFile(resource, $"cover_{detail.Source}{extension}", imageData);
                        context.CoverPaths[detail.Source!] = coverPath;
                        logCollector.LogInfo(EnhancementLogEvent.FileSaved,
                            $"Cover saved: {coverPath}",
                            new { CoverPath = coverPath, Source = detail.Source });
                        coverSaved = true;
                    }

                    if (!posterSaved && !string.IsNullOrEmpty(detail.PosterUrl))
                    {
                        logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                            $"Downloading poster from {detail.Source}",
                            new { Url = detail.PosterUrl, Source = detail.Source });

                        var imageData = await airavClient.HttpClient.GetByteArrayAsync(detail.PosterUrl, ct);

                        logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
                            $"Poster downloaded from {detail.Source} ({imageData.Length} bytes)",
                            new { Url = detail.PosterUrl, Source = detail.Source, Size = imageData.Length });

                        var extension = Path.GetExtension(detail.PosterUrl.Split('?')[0]) ?? ".jpg";
                        var posterPath = await SaveFile(resource, $"poster_{detail.Source}{extension}", imageData);
                        context.PosterPaths[detail.Source!] = posterPath;
                        logCollector.LogInfo(EnhancementLogEvent.FileSaved,
                            $"Poster saved: {posterPath}",
                            new { PosterPath = posterPath, Source = detail.Source });
                        posterSaved = true;
                    }
                }
                catch (Exception ex)
                {
                    logCollector.LogWarning(EnhancementLogEvent.Error,
                        $"Failed to download images from {detail.Source}: {ex.Message}",
                        new { Source = detail.Source, Error = ex.Message });
                    Logger.LogDebug($"Failed to download images from {detail.Source}: {ex.Message}");
                }
            }

            return context;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error building AV enhancer context");
            return null;
        }
    }

    protected override async Task<List<EnhancementTargetValue<AvEnhancerTarget>>> ConvertContextByTargets(
        AvEnhancerContext context, EnhancementLogCollector logCollector, CancellationToken ct)
    {
        var enhancements = new List<EnhancementTargetValue<AvEnhancerTarget>>();

        foreach (var target in SpecificEnumUtils<AvEnhancerTarget>.Values)
        {
            switch (target)
            {
                case AvEnhancerTarget.Number:
                    AddStringEnhancement(context.Details, d => d.Number, target, enhancements);
                    break;
                case AvEnhancerTarget.Title:
                    AddStringEnhancement(context.Details, d => d.Title, target, enhancements);
                    break;
                case AvEnhancerTarget.OriginalTitle:
                    AddStringEnhancement(context.Details, d => d.OriginalTitle, target, enhancements);
                    break;
                case AvEnhancerTarget.Actor:
                    AddStringEnhancement(context.Details, d => d.Actor, target, enhancements);
                    break;
                case AvEnhancerTarget.Tags:
                    AddListStringEnhancement(context.Details, d => d.Tag?.Split(',', StringSplitOptions.RemoveEmptyEntries), target, enhancements);
                    break;
                case AvEnhancerTarget.Release:
                    AddStringEnhancement(context.Details, d => d.Release, target, enhancements);
                    break;
                case AvEnhancerTarget.Year:
                    AddStringEnhancement(context.Details, d => d.Year, target, enhancements);
                    break;
                case AvEnhancerTarget.Studio:
                    AddStringEnhancement(context.Details, d => d.Studio, target, enhancements);
                    break;
                case AvEnhancerTarget.Publisher:
                    AddStringEnhancement(context.Details, d => d.Publisher, target, enhancements);
                    break;
                case AvEnhancerTarget.Series:
                    AddStringEnhancement(context.Details, d => d.Series, target, enhancements);
                    break;
                case AvEnhancerTarget.Runtime:
                    AddStringEnhancement(context.Details, d => d.Runtime, target, enhancements);
                    break;
                case AvEnhancerTarget.Director:
                    AddStringEnhancement(context.Details, d => d.Director, target, enhancements);
                    break;
                case AvEnhancerTarget.Source:
                    AddStringEnhancement(context.Details, d => d.Source, target, enhancements);
                    break;
                case AvEnhancerTarget.Cover:
                    if (context.CoverPaths.Any())
                    {
                        var coverPaths = context.CoverPaths.Values.ToList();
                        enhancements.Add(new EnhancementTargetValue<AvEnhancerTarget>(target, null, 
                            new ListStringValueBuilder(coverPaths)));
                    }
                    break;
                case AvEnhancerTarget.Poster:
                    if (context.PosterPaths.Any())
                    {
                        var posterPaths = context.PosterPaths.Values.ToList();
                        enhancements.Add(new EnhancementTargetValue<AvEnhancerTarget>(target, null,
                            new ListStringValueBuilder(posterPaths)));
                    }
                    break;
                case AvEnhancerTarget.Website:
                    AddStringEnhancement(context.Details, d => d.Website, target, enhancements);
                    break;
                case AvEnhancerTarget.Mosaic:
                    AddStringEnhancement(context.Details, d => d.Mosaic, target, enhancements);
                    break;
            }
        }

        return enhancements;
    }

    private static void AddStringEnhancement(List<IAvDetail> details, Func<IAvDetail, string?> selector, 
        AvEnhancerTarget target, List<EnhancementTargetValue<AvEnhancerTarget>> enhancements)
    {
        var value = details.Select(selector).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (!string.IsNullOrEmpty(value))
        {
            enhancements.Add(new EnhancementTargetValue<AvEnhancerTarget>(target, null, 
                new StringValueBuilder(value)));
        }
    }

    private static void AddListStringEnhancement(List<IAvDetail> details, Func<IAvDetail, string[]?> selector,
        AvEnhancerTarget target, List<EnhancementTargetValue<AvEnhancerTarget>> enhancements)
    {
        var values = details.SelectMany(d => selector(d) ?? []).Distinct().Where(v => !string.IsNullOrEmpty(v)).ToList();
        if (values.Any())
        {
            enhancements.Add(new EnhancementTargetValue<AvEnhancerTarget>(target, null,
                new ListStringValueBuilder(values)));
        }
    }

}