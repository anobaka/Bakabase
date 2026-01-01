using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Components;
using Bakabase.Modules.Enhancer.Extensions;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Tmdb;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Tmdb;

public class TmdbEnhancer(ILoggerFactory loggerFactory, TmdbClient client, IFileManager fileManager, IStandardValueService standardValueService, ISpecialTextService specialTextService, IServiceProvider serviceProvider)
    : AbstractKeywordEnhancer<TmdbEnhancerTarget, TmdbEnhancerContext, object?>(loggerFactory, fileManager, standardValueService, specialTextService, serviceProvider)
{
    protected override async Task<TmdbEnhancerContext?> BuildContextInternal(string keyword, Resource resource, EnhancerFullOptions options,
        EnhancementLogCollector logCollector, CancellationToken ct)
    {
        var searchUrl = TmdbUrlBuilder.SearchMovie(keyword);
        logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
            $"Searching TMDB",
            new { Url = searchUrl, Query = keyword });

        var detail = await client.SearchAndGetFirst(keyword);

        logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
            detail != null ? $"Found movie: {detail.Title}" : $"No results found for: {keyword}",
            new { Url = searchUrl, Found = detail != null, Title = detail?.Title, OriginalTitle = detail?.OriginalTitle });

        if (detail != null)
        {
            var ctx = new TmdbEnhancerContext
            {
                Title = detail.Title,
                OriginalTitle = detail.OriginalTitle,
                Overview = detail.Overview,
                Rating = detail.VoteAverage,
                VoteCount = detail.VoteCount,
                ReleaseDate = detail.ReleaseDate,
                Runtime = detail.Runtime > 0 ? detail.Runtime : null,
                Status = detail.Status,
                Tagline = detail.Tagline,
                Budget = detail.Budget > 0 ? detail.Budget : null,
                Revenue = detail.Revenue > 0 ? detail.Revenue : null,
                Genres = detail.Genres?.Select(g => new TagValue(null, g.Name ?? "")).Where(g => !string.IsNullOrEmpty(g.Name)).ToList(),
                ProductionCountries = detail.ProductionCountries,
                SpokenLanguages = detail.SpokenLanguages
            };

            if (!string.IsNullOrEmpty(detail.PosterPath))
            {
                try
                {
                    var posterUrl = TmdbUrlBuilder.GetPosterUrl(detail.PosterPath);
                    logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                        "Downloading poster",
                        new { Url = posterUrl });

                    var imageData = await client.HttpClient.GetByteArrayAsync(posterUrl, ct);

                    logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
                        $"Poster downloaded ({imageData.Length} bytes)",
                        new { Url = posterUrl, Size = imageData.Length });

                    var extension = Path.GetExtension(detail.PosterPath) ?? ".jpg";
                    ctx.CoverPath = await SaveFile(resource, $"cover{extension}", imageData);
                    logCollector.LogInfo(EnhancementLogEvent.FileSaved,
                        $"Poster saved: {ctx.CoverPath}",
                        new { CoverPath = ctx.CoverPath });
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to download poster image from TMDB");
                    logCollector.LogWarning(EnhancementLogEvent.Error,
                        $"Failed to download poster: {ex.Message}",
                        new { Error = ex.Message });
                }
            }

            if (!string.IsNullOrEmpty(detail.BackdropPath))
            {
                try
                {
                    var backdropUrl = TmdbUrlBuilder.GetBackdropUrl(detail.BackdropPath);
                    logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                        "Downloading backdrop",
                        new { Url = backdropUrl });

                    var imageData = await client.HttpClient.GetByteArrayAsync(backdropUrl, ct);

                    logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
                        $"Backdrop downloaded ({imageData.Length} bytes)",
                        new { Url = backdropUrl, Size = imageData.Length });

                    var extension = Path.GetExtension(detail.BackdropPath) ?? ".jpg";
                    ctx.BackdropPath = await SaveFile(resource, $"backdrop{extension}", imageData);
                    logCollector.LogInfo(EnhancementLogEvent.FileSaved,
                        $"Backdrop saved: {ctx.BackdropPath}",
                        new { BackdropPath = ctx.BackdropPath });
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to download backdrop image from TMDB");
                    logCollector.LogWarning(EnhancementLogEvent.Error,
                        $"Failed to download backdrop: {ex.Message}",
                        new { Error = ex.Message });
                }
            }

            return ctx;
        }

        return null;
    }

    protected override EnhancerId TypedId => EnhancerId.Tmdb;

    protected override async Task<List<EnhancementTargetValue<TmdbEnhancerTarget>>> ConvertContextByTargets(
        TmdbEnhancerContext context, EnhancementLogCollector logCollector, CancellationToken ct)
    {
        var enhancements = new List<EnhancementTargetValue<TmdbEnhancerTarget>>();
        foreach (var target in SpecificEnumUtils<TmdbEnhancerTarget>.Values)
        {
            IStandardValueBuilder? valueBuilder = target switch
            {
                TmdbEnhancerTarget.Title => new StringValueBuilder(context.Title),
                TmdbEnhancerTarget.OriginalTitle => new StringValueBuilder(context.OriginalTitle),
                TmdbEnhancerTarget.Overview => new StringValueBuilder(context.Overview),
                TmdbEnhancerTarget.Rating => new DecimalValueBuilder(context.Rating),
                TmdbEnhancerTarget.VoteCount => new DecimalValueBuilder(context.VoteCount),
                TmdbEnhancerTarget.ReleaseDate => new DateTimeValueBuilder(context.ReleaseDate),
                TmdbEnhancerTarget.Runtime => new DecimalValueBuilder(context.Runtime),
                TmdbEnhancerTarget.Genres => new ListTagValueBuilder(context.Genres),
                TmdbEnhancerTarget.ProductionCountries => new ListStringValueBuilder(context.ProductionCountries),
                TmdbEnhancerTarget.SpokenLanguages => new ListStringValueBuilder(context.SpokenLanguages),
                TmdbEnhancerTarget.Status => new StringValueBuilder(context.Status),
                TmdbEnhancerTarget.Tagline => new StringValueBuilder(context.Tagline),
                TmdbEnhancerTarget.Budget => new DecimalValueBuilder(context.Budget),
                TmdbEnhancerTarget.Revenue => new DecimalValueBuilder(context.Revenue),
                TmdbEnhancerTarget.Cover => new ListStringValueBuilder(
                    string.IsNullOrEmpty(context.CoverPath) ? null : [context.CoverPath]),
                TmdbEnhancerTarget.Backdrop => new ListStringValueBuilder(
                    string.IsNullOrEmpty(context.BackdropPath) ? null : [context.BackdropPath]),
                _ => throw new ArgumentOutOfRangeException()
            };

            if (valueBuilder?.Value != null)
            {
                enhancements.Add(new EnhancementTargetValue<TmdbEnhancerTarget>(target, null, valueBuilder));
            }
        }

        return enhancements;
    }
}