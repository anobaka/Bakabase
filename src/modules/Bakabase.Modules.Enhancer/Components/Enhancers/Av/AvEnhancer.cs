using System.Text.Json;
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
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsex;
using Bakabase.Modules.ThirdParty.ThirdParties.Avsox;
using Bakabase.Modules.ThirdParty.ThirdParties.CNMDB;
using Bakabase.Modules.ThirdParty.ThirdParties.Dahlia;
using Bakabase.Modules.ThirdParty.ThirdParties.Dmm;
using Bakabase.Modules.ThirdParty.ThirdParties.FC2;
using Bakabase.Modules.ThirdParty.ThirdParties.Faleno;
using Bakabase.Modules.ThirdParty.ThirdParties.Fantastica;
using Bakabase.Modules.ThirdParty.ThirdParties.Fc2hub;
using Bakabase.Modules.ThirdParty.ThirdParties.Freejavbt;
using Bakabase.Modules.ThirdParty.ThirdParties.GetchuDl;
using Bakabase.Modules.ThirdParty.ThirdParties.Iqqtv;
using Bakabase.Modules.ThirdParty.ThirdParties.Jav321;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus;
using Bakabase.Modules.ThirdParty.ThirdParties.Javday;
using Bakabase.Modules.ThirdParty.ThirdParties.Javdb;
using Bakabase.Modules.ThirdParty.ThirdParties.Javlibrary;
using Bakabase.Modules.ThirdParty.ThirdParties.Lulubar;
using Bakabase.Modules.ThirdParty.ThirdParties.Mmtv;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Av;

public class AvEnhancer(
    ILoggerFactory loggerFactory,
    IFileManager fileManager,
    AiravClient airavClient,
    AvsexClient avsexClient,
    AvsoxClient avsoxClient,
    CNMDBClient cnmdbClient,
    DmmClient dmmClient,
    DahliaClient dahliaClient,
    FC2Client fc2Client,
    FalenoClient falenoClient,
    FantasticaClient fantasticaClient,
    Fc2hubClient fc2hubClient,
    FreejavbtClient freejavbtClient,
    GetchuDlClient getchuDlClient,
    IqqtvClient iqqtvClient,
    Jav321Client jav321Client,
    JavbusClient javbusClient,
    JavdayClient javdayClient,
    JavdbClient javdbClient,
    JavlibraryClient javlibraryClient,
    LulubarClient lulubarClient,
    MmtvClient mmtvClient,
    IAvSourceOptionsProvider avOptionsProvider,
    IStandardValueService standardValueService, ISpecialTextService specialTextService, IServiceProvider serviceProvider)
    : AbstractKeywordEnhancer<AvEnhancerTarget, AvEnhancerContext, IKeywordEnhancerOptions>(loggerFactory, fileManager, standardValueService, specialTextService, serviceProvider)
{
    protected override EnhancerId TypedId => EnhancerId.Av;

    protected override async Task<AvEnhancerContext?> BuildContextInternal(string keyword, Resource resource, IKeywordEnhancerOptions options,
        EnhancementLogCollector logCollector, CancellationToken ct)
    {
        try
        {
            // Define all clients that implement SearchAndParseVideo returning IAvDetail
            var clients = new Dictionary<string, Func<string, string?, Task<IAvDetail?>>>
            {
                { "airav", (num, url) => airavClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "avsex", (num, url) => avsexClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "avsox", (num, url) => avsoxClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "cnmdb", (num, url) => cnmdbClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "dmm", (num, url) => dmmClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "dahlia", (num, url) => dahliaClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "fc2", (num, url) => fc2Client.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "faleno", (num, url) => falenoClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "fantastica", (num, url) => fantasticaClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "fc2hub", (num, url) => fc2hubClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "freejavbt", (num, url) => freejavbtClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "getchudl", (num, url) => getchuDlClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "iqqtv", (num, url) => iqqtvClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "jav321", (num, url) => jav321Client.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "javbus", (num, url) => javbusClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "javday", (num, url) => javdayClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "javdb", (num, url) => javdbClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "javlibrary", (num, url) => javlibraryClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "lulubar", (num, url) => lulubarClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
                { "mmtv", (num, url) => mmtvClient.SearchAndParseVideo(num, appointUrl: url).ContinueWith(t => (IAvDetail?)t.Result, ct) },
            };

            var disabledSources = clients.Keys
                .Where(k => !avOptionsProvider.Resolve(k).Enabled)
                .ToArray();
            foreach (var k in disabledSources)
            {
                clients.Remove(k);
            }

            if (clients.Count == 0)
            {
                logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                    "All AV data sources are disabled; skipping search",
                    new { Keyword = keyword, DisabledSources = disabledSources });
                return null;
            }

            logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                $"Searching AV with keyword: {keyword} using {clients.Count} data sources",
                new
                {
                    Keyword = keyword,
                    SourceCount = clients.Count,
                    Sources = clients.Keys.ToArray(),
                    DisabledSources = disabledSources,
                });

            var context = new AvEnhancerContext();

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

            // Dump per-source parsed results to a debug file for diagnosis
            try
            {
                var perSourceData = context.Details.Select(d => new Dictionary<string, string?>
                {
                    ["Source"] = d.Source,
                    ["Number"] = d.Number,
                    ["Title"] = d.Title,
                    ["OriginalTitle"] = d.OriginalTitle,
                    ["Actor"] = d.Actor,
                    ["Publisher"] = d.Publisher,
                    ["Studio"] = d.Studio,
                    ["Series"] = d.Series,
                    ["Director"] = d.Director,
                    ["Tag"] = d.Tag,
                    ["Release"] = d.Release,
                    ["Year"] = d.Year,
                    ["Runtime"] = d.Runtime,
                    ["Mosaic"] = d.Mosaic,
                    ["Website"] = d.Website,
                    ["SearchUrl"] = d.SearchUrl,
                    ["CoverUrl"] = d.CoverUrl,
                    ["PosterUrl"] = d.PosterUrl,
                }).ToList();
                var debugJson = JsonSerializer.Serialize(perSourceData, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                var debugDir = Path.Combine(Path.GetTempPath(), "bakabase_av_debug");
                Directory.CreateDirectory(debugDir);
                var debugFilePath = Path.Combine(debugDir, $"{keyword}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                await File.WriteAllTextAsync(debugFilePath, debugJson, ct);
                Logger.LogInformation("AV enhancer per-source debug results saved to {DebugFilePath}", debugFilePath);
                logCollector.LogInfo(EnhancementLogEvent.DataFetched,
                    $"Per-source debug results saved to {debugFilePath}",
                    new { DebugFilePath = debugFilePath, SourceCount = perSourceData.Count });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to save AV enhancer debug results");
            }

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
        AvEnhancerContext context, IKeywordEnhancerOptions options, EnhancementLogCollector logCollector, CancellationToken ct)
    {
        var enhancements = new List<EnhancementTargetValue<AvEnhancerTarget>>();

        var preferredSourcesByTarget = options.TargetOptions?
            .Where(o => o.PreferredSources != null)
            .GroupBy(o => (AvEnhancerTarget)o.Target)
            .ToDictionary(g => g.Key, g => g.First().PreferredSources!);

        foreach (var target in SpecificEnumUtils<AvEnhancerTarget>.Values)
        {
            var orderedDetails = OrderDetailsForTarget(context.Details, target, preferredSourcesByTarget);

            switch (target)
            {
                case AvEnhancerTarget.Number:
                    AddStringEnhancement(orderedDetails, d => d.Number, target, enhancements);
                    break;
                case AvEnhancerTarget.Title:
                    AddStringEnhancement(orderedDetails, d => d.Title, target, enhancements);
                    break;
                case AvEnhancerTarget.OriginalTitle:
                    AddStringEnhancement(orderedDetails, d => d.OriginalTitle, target, enhancements);
                    break;
                case AvEnhancerTarget.Actor:
                    AddListStringEnhancement(orderedDetails, d => d.Actor?.Split(',', StringSplitOptions.RemoveEmptyEntries), target, enhancements);
                    break;
                case AvEnhancerTarget.Tags:
                    AddListStringEnhancement(orderedDetails, d => d.Tag?.Split(',', StringSplitOptions.RemoveEmptyEntries), target, enhancements);
                    break;
                case AvEnhancerTarget.Release:
                    AddStringEnhancement(orderedDetails, d => d.Release, target, enhancements);
                    break;
                case AvEnhancerTarget.Year:
                    AddStringEnhancement(orderedDetails, d => d.Year, target, enhancements);
                    break;
                case AvEnhancerTarget.Studio:
                    AddStringEnhancement(orderedDetails, d => d.Studio, target, enhancements);
                    break;
                case AvEnhancerTarget.Publisher:
                    AddListStringEnhancement(orderedDetails, d => d.Publisher?.Split(',', StringSplitOptions.RemoveEmptyEntries), target, enhancements);
                    break;
                case AvEnhancerTarget.Series:
                    AddListStringEnhancement(orderedDetails, d => d.Series?.Split(',', StringSplitOptions.RemoveEmptyEntries), target, enhancements);
                    break;
                case AvEnhancerTarget.Runtime:
                    AddStringEnhancement(orderedDetails, d => d.Runtime, target, enhancements);
                    break;
                case AvEnhancerTarget.Director:
                    AddListStringEnhancement(orderedDetails, d => d.Director?.Split(',', StringSplitOptions.RemoveEmptyEntries), target, enhancements);
                    break;
                case AvEnhancerTarget.Source:
                    AddStringEnhancement(orderedDetails, d => d.Source, target, enhancements);
                    break;
                case AvEnhancerTarget.Cover:
                {
                    var coverPaths = OrderPathsForTarget(context.CoverPaths, target, preferredSourcesByTarget);
                    if (coverPaths.Count > 0)
                    {
                        enhancements.Add(new EnhancementTargetValue<AvEnhancerTarget>(target, null,
                            new ListStringValueBuilder(coverPaths)));
                    }
                    break;
                }
                case AvEnhancerTarget.Poster:
                {
                    var posterPaths = OrderPathsForTarget(context.PosterPaths, target, preferredSourcesByTarget);
                    if (posterPaths.Count > 0)
                    {
                        enhancements.Add(new EnhancementTargetValue<AvEnhancerTarget>(target, null,
                            new ListStringValueBuilder(posterPaths)));
                    }
                    break;
                }
                case AvEnhancerTarget.Website:
                    AddStringEnhancement(orderedDetails, d => d.Website, target, enhancements);
                    break;
                case AvEnhancerTarget.Mosaic:
                    AddStringEnhancement(orderedDetails, d => d.Mosaic, target, enhancements);
                    break;
            }
        }

        return enhancements;
    }

    /// <summary>
    /// Reorders/filters the search results for one target according to the user's per-target
    /// PreferredSources list. When PreferredSources is unset for the target, the original
    /// context order is preserved (effectively the built-in source order).
    /// </summary>
    private static IReadOnlyList<IAvDetail> OrderDetailsForTarget(
        IReadOnlyList<IAvDetail> details,
        AvEnhancerTarget target,
        Dictionary<AvEnhancerTarget, List<string>>? preferredSourcesByTarget)
    {
        if (preferredSourcesByTarget == null ||
            !preferredSourcesByTarget.TryGetValue(target, out var preferred))
        {
            return details;
        }

        var bySource = details
            .Where(d => !string.IsNullOrEmpty(d.Source))
            .GroupBy(d => d.Source!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var ordered = new List<IAvDetail>(preferred.Count);
        foreach (var src in preferred)
        {
            if (bySource.TryGetValue(src, out var d))
            {
                ordered.Add(d);
            }
        }
        return ordered;
    }

    private static List<string> OrderPathsForTarget(
        Dictionary<string, string> pathsBySource,
        AvEnhancerTarget target,
        Dictionary<AvEnhancerTarget, List<string>>? preferredSourcesByTarget)
    {
        if (preferredSourcesByTarget == null ||
            !preferredSourcesByTarget.TryGetValue(target, out var preferred))
        {
            return pathsBySource.Values.ToList();
        }

        var ordered = new List<string>(preferred.Count);
        foreach (var src in preferred)
        {
            if (pathsBySource.TryGetValue(src, out var p))
            {
                ordered.Add(p);
            }
        }
        return ordered;
    }

    private static void AddStringEnhancement(IReadOnlyList<IAvDetail> details, Func<IAvDetail, string?> selector,
        AvEnhancerTarget target, List<EnhancementTargetValue<AvEnhancerTarget>> enhancements)
    {
        var value = details.Select(selector).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (!string.IsNullOrEmpty(value))
        {
            value = DeduplicateString(value);
            enhancements.Add(new EnhancementTargetValue<AvEnhancerTarget>(target, null,
                new StringValueBuilder(value)));
        }
    }

    /// <summary>
    /// Detects and removes duplicated text caused by CsQuery .Text() concatenating
    /// text from multiple matching elements (e.g., mobile and desktop versions).
    /// For example, "TitleTitle" or "Title Title" becomes "Title".
    /// </summary>
    private static string DeduplicateString(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 2)
        {
            return value;
        }

        var trimmed = value.Trim();
        var len = trimmed.Length;

        // Check for exact duplication: "TitleTitle"
        if (len % 2 == 0)
        {
            var half = trimmed.Substring(0, len / 2);
            if (trimmed.Substring(len / 2) == half)
            {
                return half;
            }
        }

        // Check for duplication with single space: "Title Title"
        var spaceIdx = trimmed.IndexOf(' ', trimmed.Length / 3);
        while (spaceIdx > 0 && spaceIdx < trimmed.Length - 1)
        {
            var left = trimmed.Substring(0, spaceIdx);
            var right = trimmed.Substring(spaceIdx + 1);
            if (left == right)
            {
                return left;
            }
            spaceIdx = trimmed.IndexOf(' ', spaceIdx + 1);
        }

        return value;
    }

    private static void AddListStringEnhancement(IReadOnlyList<IAvDetail> details, Func<IAvDetail, string[]?> selector,
        AvEnhancerTarget target, List<EnhancementTargetValue<AvEnhancerTarget>> enhancements)
    {
        var values = details.SelectMany(d => selector(d) ?? []).Select(v => v.Trim()).Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
        if (values.Any())
        {
            enhancements.Add(new EnhancementTargetValue<AvEnhancerTarget>(target, null,
                new ListStringValueBuilder(values)));
        }
    }

}