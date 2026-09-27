using System.Text.Json;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Network;
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
using Bakabase.Abstractions.Components.Text;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Av;

public class AvEnhancer(
    ILoggerFactory loggerFactory,
    IFileManager fileManager,
    IEnumerable<IAvClient> avClients,
    IHttpClientFactory httpClientFactory,
    IAvSourceOptionsProvider avOptionsProvider,
    IStandardValueService standardValueService, ITextOps textOps, IServiceProvider serviceProvider)
    : AbstractKeywordEnhancer<AvEnhancerTarget, AvEnhancerContext, IKeywordEnhancerOptions>(loggerFactory, fileManager, standardValueService, textOps, serviceProvider)
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

            // Search in parallel, then write logs sequentially: the log collector is not thread safe.
            var tasks = clients.Select(async kvp =>
            {
                using var captureScope = HttpInteractionCapture.Begin();
                try
                {
                    var detail = await kvp.Value.SearchAndParseVideo(keyword);
                    return (SourceId: kvp.Key, Detail: detail, Error: (Exception?)null,
                        Interactions: HttpInteractionCapture.Current?.ToArray() ?? []);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return (SourceId: kvp.Key, Detail: (IAvDetail?)null, Error: ex,
                        Interactions: HttpInteractionCapture.Current?.ToArray() ?? []);
                }
            });

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
            {
                // Never persist request headers: the HTTP capture includes source cookies.
                var requests = result.Interactions.Select(i => new
                {
                    i.Method, i.Url, i.ResponseStatusCode, i.Error, i.DurationMs
                }).ToArray();
                var hasFailedRequest = result.Interactions.Any(i => i.Error != null || i.ResponseStatusCode >= 400);

                if (result.Error != null)
                {
                    logCollector.LogWarning(EnhancementLogEvent.Error,
                        $"AV source {result.SourceId} search failed: {result.Error.Message}",
                        new { Source = result.SourceId, ExceptionType = result.Error.GetType().Name, Requests = requests });
                    Logger.LogDebug(result.Error, "AV source {Source} search failed", result.SourceId);
                }
                else if (result.Detail == null)
                {
                    var message = hasFailedRequest
                        ? $"AV source {result.SourceId} returned no result after an HTTP failure"
                        : $"AV source {result.SourceId} returned no detail (no match or parsing failed)";
                    logCollector.LogWarning(EnhancementLogEvent.DataFetched, message,
                        new { Source = result.SourceId, Requests = requests });
                }
                else
                {
                    logCollector.LogInfo(EnhancementLogEvent.DataFetched,
                        $"AV source {result.SourceId} returned a result",
                        new
                        {
                            Source = result.SourceId, DetailSource = result.Detail.Source,
                            result.Detail.SearchUrl, result.Detail.CoverUrl, result.Detail.PosterUrl,
                            Requests = requests
                        });
                    if (!string.Equals(result.SourceId, result.Detail.Source, StringComparison.OrdinalIgnoreCase))
                    {
                        logCollector.LogWarning(EnhancementLogEvent.DataFetched,
                            $"AV source {result.SourceId} returned an unexpected source id: {result.Detail.Source}",
                            new { ExpectedSource = result.SourceId, ActualSource = result.Detail.Source });
                    }
                }
            }
            context.Details = results.Where(r => r.Detail != null).Select(r => r.Detail!).ToList();

            logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
                $"Found {context.Details.Count} results from different sources",
                new {
                    ResultCount = context.Details.Count,
                    SuccessfulSources = results.Where(r => r.Detail != null).Select(r => r.SourceId).ToArray(),
                    FailedSources = results.Where(r => r.Error != null).Select(r => r.SourceId).ToArray(),
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

            var preferredSourcesByTarget = avOptionsProvider.GetPreferredSourcesByTarget();
            await DownloadImageForTarget(context, resource, AvEnhancerTarget.Cover, d => d.CoverUrl,
                context.CoverPaths, preferredSourcesByTarget, logCollector, ct);
            await DownloadImageForTarget(context, resource, AvEnhancerTarget.Poster, d => d.PosterUrl,
                context.PosterPaths, preferredSourcesByTarget, logCollector, ct);

            return context;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logCollector.LogError(EnhancementLogEvent.Error,
                $"Failed to build AV enhancement context: {ex.Message}",
                new { ExceptionType = ex.GetType().Name, Error = ex.Message });
            Logger.LogError(ex, "Error building AV enhancer context");
            return null;
        }
    }

    private async Task DownloadImageForTarget(AvEnhancerContext context, Resource resource,
        AvEnhancerTarget target, Func<IAvDetail, string?> urlSelector, Dictionary<string, string> pathsBySource,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? preferredSourcesByTarget,
        EnhancementLogCollector logCollector, CancellationToken ct)
    {
        var orderedDetails = OrderDetailsForTarget(context.Details, target, preferredSourcesByTarget);
        var configuredSources = preferredSourcesByTarget != null &&
                                preferredSourcesByTarget.TryGetValue((int)target, out var preferred)
            ? preferred
            : null;
        logCollector.LogInfo(EnhancementLogEvent.Configuration,
            $"AV {target} source order: {string.Join(", ", orderedDetails.Select(d => d.Source))}",
            new { Target = target.ToString(), PreferredSources = configuredSources,
                AvailableSources = orderedDetails.Select(d => d.Source).ToArray() });

        foreach (var detail in orderedDetails)
        {
            var source = detail.Source;
            var url = urlSelector(detail);
            if (string.IsNullOrWhiteSpace(source))
            {
                logCollector.LogWarning(EnhancementLogEvent.DataFetched,
                    $"Skipping AV {target} image because the result has no source id");
                continue;
            }
            if (string.IsNullOrWhiteSpace(url))
            {
                logCollector.LogInfo(EnhancementLogEvent.DataFetched,
                    $"AV source {source} has no {target} URL",
                    new { Target = target.ToString(), Source = source });
                continue;
            }

            var stage = "download";
            int? statusCode = null;
            string? contentType = null;
            try
            {
                logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                    $"Downloading AV {target} from {source}",
                    new { Target = target.ToString(), Source = source, Url = url });
                using var response = await httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default)
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                statusCode = (int)response.StatusCode;
                contentType = response.Content.Headers.ContentType?.MediaType;
                response.EnsureSuccessStatusCode();
                if (contentType != null && (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                                            contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException($"Image URL returned {contentType} instead of image data");
                }
                var imageData = await response.Content.ReadAsByteArrayAsync(ct);
                if (imageData.Length == 0)
                {
                    throw new InvalidDataException("Image URL returned an empty response");
                }
                logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
                    $"AV {target} downloaded from {source} ({imageData.Length} bytes)",
                    new { Target = target.ToString(), Source = source, Url = url, StatusCode = statusCode,
                        ContentType = contentType, Size = imageData.Length });

                stage = "save";
                var extension = Path.GetExtension(url.Split('?')[0]);
                if (string.IsNullOrEmpty(extension)) extension = ".jpg";
                var path = await SaveFile(resource, $"{target.ToString().ToLowerInvariant()}_{source}{extension}", imageData);
                pathsBySource[source] = path;
                logCollector.LogInfo(EnhancementLogEvent.FileSaved,
                    $"AV {target} saved from {source}: {path}",
                    new { Target = target.ToString(), Source = source, Url = url, Path = path });
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logCollector.LogWarning(EnhancementLogEvent.Error,
                    $"Failed to {stage} AV {target} from {source}: {ex.Message}",
                    new { Target = target.ToString(), Source = source, Url = url,
                        StatusCode = statusCode, ContentType = contentType,
                        ExceptionType = ex.GetType().Name, Error = ex.Message });
                Logger.LogDebug(ex, "Failed to {Stage} AV {Target} from {Source}", stage, target, source);
            }
        }

        logCollector.LogWarning(EnhancementLogEvent.TargetConverted,
            $"No AV {target} image could be saved from the selected sources",
            new { Target = target.ToString(), PreferredSources = configuredSources,
                AvailableSources = orderedDetails.Select(d => d.Source).ToArray() });
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
                        logCollector.LogInfo(EnhancementLogEvent.TargetConverted,
                            "AV Cover image added to enhancement values",
                            new { Target = target.ToString(), Paths = coverPaths });
                    }
                    else
                    {
                        logCollector.LogWarning(EnhancementLogEvent.TargetConverted,
                            "AV Cover has no saved image from the selected sources",
                            new { Target = target.ToString(), SavedSources = context.CoverPaths.Keys.ToArray() });
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
                        logCollector.LogInfo(EnhancementLogEvent.TargetConverted,
                            "AV Poster image added to enhancement values",
                            new { Target = target.ToString(), Paths = posterPaths });
                    }
                    else
                    {
                        logCollector.LogWarning(EnhancementLogEvent.TargetConverted,
                            "AV Poster has no saved image from the selected sources",
                            new { Target = target.ToString(), SavedSources = context.PosterPaths.Keys.ToArray() });
                    }
                    break;
                }
                case AvEnhancerTarget.Website:
                    AddStringEnhancement(orderedDetails, d => d.Website, target, enhancements);
                    break;
                case AvEnhancerTarget.Mosaic:
                    AddBooleanEnhancement(orderedDetails, d => ParseMosaic(d.Mosaic), target, enhancements);
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

        var pathsBySourceIgnoreCase = new Dictionary<string, string>(pathsBySource, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>(preferred.Count);
        foreach (var src in preferred)
        {
            if (pathsBySourceIgnoreCase.TryGetValue(src, out var p))
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

    private static void AddBooleanEnhancement(IReadOnlyList<IAvDetail> details, Func<IAvDetail, bool?> selector,
        AvEnhancerTarget target, List<EnhancementTargetValue<AvEnhancerTarget>> enhancements)
    {
        // Use the first source (respecting preferred-source ordering) that yields a
        // definitive value; skip sources whose value can't be interpreted.
        foreach (var d in details)
        {
            var value = selector(d);
            if (value.HasValue)
            {
                enhancements.Add(new EnhancementTargetValue<AvEnhancerTarget>(target, null,
                    new BooleanValueBuilder(value)));
                return;
            }
        }
    }

    /// <summary>
    /// Interprets the raw mosaic string from AV sources (e.g. "有码"/"无码"/"国产"/"同人"/"里番")
    /// as a "has mosaic" (censored) boolean:
    ///   - uncensored markers (无码/無碼/無修正/uncensored) → false
    ///   - censored markers (有码/有碼)                     → true
    ///   - anything else (国产/同人/里番/empty/unknown)      → null
    /// Returning null avoids forcing an arbitrary true/false for values that don't actually
    /// describe the mosaic state, which is why mapping these strings straight to a Boolean
    /// property used to always yield "true".
    /// </summary>
    internal static bool? ParseMosaic(string? mosaic)
    {
        if (string.IsNullOrWhiteSpace(mosaic))
        {
            return null;
        }

        var v = mosaic.Trim();

        if (v.Contains("无码") || v.Contains("無碼") || v.Contains("無修正") ||
            v.Contains("uncensored", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (v.Contains("有码") || v.Contains("有碼"))
        {
            return true;
        }

        return null;
    }

}
