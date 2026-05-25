using System.Text.Json;
using Bakabase.Abstractions.Components.Configuration;
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
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Av;

public class AvEnhancer(
    ILoggerFactory loggerFactory,
    IFileManager fileManager,
    IEnumerable<IAvClient> avClients,
    IHttpClientFactory httpClientFactory,
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
            // SourceId on each IAvClient MUST match IAvDetail.Source (see AvSourceIds) —
            // preferred-source filtering compares against d.Source and silently drops
            // anything that can't be looked up. Adding a new client only requires a
            // DI registration (see ThirdPartyExtensions); this dispatcher picks it up
            // automatically.
            var clients = avClients.ToDictionary(c => c.SourceId, c => c);

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
                    var result = await kvp.Value.SearchAndParseVideo(keyword);
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
                var debugJson = JsonSerializer.Serialize(context.Details, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                var debugDir = Path.Combine(Path.GetTempPath(), "bakabase_av_debug");
                Directory.CreateDirectory(debugDir);
                var debugFilePath = Path.Combine(debugDir, $"{keyword}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                await File.WriteAllTextAsync(debugFilePath, debugJson, ct);
                Logger.LogInformation("AV enhancer per-source debug results saved to {DebugFilePath}", debugFilePath);
                logCollector.LogInfo(EnhancementLogEvent.DataFetched,
                    $"Per-source debug results saved to {debugFilePath}",
                    new { DebugFilePath = debugFilePath, SourceCount = context.Details.Count });
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

                        var imageData = await httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default).GetByteArrayAsync(detail.CoverUrl, ct);

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

                        var imageData = await httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default).GetByteArrayAsync(detail.PosterUrl, ct);

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

        var preferredSourcesByTarget = avOptionsProvider.GetPreferredSourcesByTarget();

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
                {
                    // Runtime from clients is a digit-only minutes string (e.g., "120"),
                    // which TimeSpan.TryParse cannot handle. Parse it explicitly.
                    foreach (var d in orderedDetails)
                    {
                        if (int.TryParse(d.Runtime, out var minutes) && minutes > 0)
                        {
                            enhancements.Add(new EnhancementTargetValue<AvEnhancerTarget>(target, null,
                                new TimeValueBuilder(TimeSpan.FromMinutes(minutes))));
                            break;
                        }
                    }
                    break;
                }
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
                case AvEnhancerTarget.Introduction:
                    AddStringEnhancement(orderedDetails, d => d.Outline, target, enhancements);
                    break;
            }
        }

        return enhancements;
    }

    /// <summary>
    /// Reorders/filters the search results for one target according to the global per-target
    /// preferred source list (from <see cref="IAvSourceOptionsProvider"/>). When no preference
    /// is set for the target, the original context order is preserved (built-in source order).
    /// </summary>
    private static IReadOnlyList<IAvDetail> OrderDetailsForTarget(
        IReadOnlyList<IAvDetail> details,
        AvEnhancerTarget target,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? preferredSourcesByTarget)
    {
        if (preferredSourcesByTarget == null ||
            !preferredSourcesByTarget.TryGetValue((int)target, out var preferred))
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
        IReadOnlyDictionary<int, IReadOnlyList<string>>? preferredSourcesByTarget)
    {
        if (preferredSourcesByTarget == null ||
            !preferredSourcesByTarget.TryGetValue((int)target, out var preferred))
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