using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Microsoft.Extensions.Logging;
using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi;

public class KodiEnhancer(ILoggerFactory loggerFactory, IFileManager fileManager)
    : AbstractEnhancer<KodiEnhancerTarget, KodiEnhancerContext, object?>(loggerFactory, fileManager)
{
    protected override async Task<KodiEnhancerContext?> BuildContext(Resource resource, EnhancerFullOptions options,
        CancellationToken ct)
    {
        try
        {
            // Look for .nfo files in the same directory as the resource
            var directory = Path.GetDirectoryName(resource.Path);
            if (string.IsNullOrEmpty(directory))
            {
                Logger.LogWarning($"Cannot determine directory for resource: {resource.Path}");
                return null;
            }

            var nfoFiles = Directory.GetFiles(directory, "*.nfo", SearchOption.TopDirectoryOnly);
            if (!nfoFiles.Any())
            {
                Logger.LogInformation($"No .nfo files found in directory: {directory}");
                return null;
            }

            // Try to find the most relevant .nfo file (same name as resource or generic)
            var resourceName = Path.GetFileNameWithoutExtension(resource.FileName);
            var targetNfoFile = nfoFiles.FirstOrDefault(f => 
                Path.GetFileNameWithoutExtension(f).Equals(resourceName, StringComparison.OrdinalIgnoreCase)) 
                ?? nfoFiles.First();

            Logger.LogInformation($"Using NFO file: {targetNfoFile}");

            var xmlContent = await File.ReadAllTextAsync(targetNfoFile, ct);
            if (string.IsNullOrEmpty(xmlContent))
            {
                Logger.LogWarning($"NFO file is empty: {targetNfoFile}");
                return null;
            }

            // Deserialize the XML content
            var serializer = new XmlSerializer(typeof(KodiEnhancerContext));
            using var reader = new StringReader(xmlContent);
            var context = serializer.Deserialize(reader) as KodiEnhancerContext;

            if (context == null)
            {
                Logger.LogWarning($"Failed to deserialize NFO file: {targetNfoFile}");
                return null;
            }

            Logger.LogInformation($"Successfully parsed NFO file with content: Movie={context.Movie != null}, TvShow={context.TvShow != null}, MusicVideo={context.MusicVideo != null}, Album={context.Album != null}, Artist={context.Artist != null}");
            return context;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Error building Kodi context for resource: {resource.Path}");
            return null;
        }
    }

    protected override EnhancerId TypedId => EnhancerId.Kodi;

    protected override async Task<List<EnhancementTargetValue<KodiEnhancerTarget>>> ConvertContextByTargets(
        KodiEnhancerContext context, CancellationToken ct)
    {
        var enhancements = new List<EnhancementTargetValue<KodiEnhancerTarget>>();

        // Process Movie data
        if (context.Movie != null)
        {
            AddEnhancement(enhancements, KodiEnhancerTarget.Title, context.Movie.Title);
            AddEnhancement(enhancements, KodiEnhancerTarget.OriginalTitle, context.Movie.OriginalTitle);
            AddEnhancement(enhancements, KodiEnhancerTarget.SortTitle, context.Movie.SortTitle);
            AddEnhancement(enhancements, KodiEnhancerTarget.Outline, context.Movie.Outline);
            AddEnhancement(enhancements, KodiEnhancerTarget.Plot, context.Movie.Plot);
            AddEnhancement(enhancements, KodiEnhancerTarget.Tagline, context.Movie.Tagline);
            AddEnhancement(enhancements, KodiEnhancerTarget.Runtime, context.Movie.Runtime);
            AddEnhancement(enhancements, KodiEnhancerTarget.Mpaa, context.Movie.Mpaa);
            AddEnhancement(enhancements, KodiEnhancerTarget.PlayCount, context.Movie.PlayCount);
            AddEnhancement(enhancements, KodiEnhancerTarget.LastPlayed, context.Movie.LastPlayed);
            AddEnhancement(enhancements, KodiEnhancerTarget.Id, context.Movie.Id);
            AddEnhancement(enhancements, KodiEnhancerTarget.Genres, context.Movie.Genres);
            AddEnhancement(enhancements, KodiEnhancerTarget.Countries, context.Movie.Countries);
            AddEnhancement(enhancements, KodiEnhancerTarget.Tags, context.Movie.Tags);
            AddEnhancement(enhancements, KodiEnhancerTarget.VideoAssetTitle, context.Movie.VideoAssetTitle);
            AddEnhancement(enhancements, KodiEnhancerTarget.VideoAssetId, context.Movie.VideoAssetId);
            AddEnhancement(enhancements, KodiEnhancerTarget.VideoAssetType, context.Movie.VideoAssetType);
            AddEnhancement(enhancements, KodiEnhancerTarget.HasVideoVersions, context.Movie.HasVideoVersions);
            AddEnhancement(enhancements, KodiEnhancerTarget.HasVideoExtras, context.Movie.HasVideoExtras);
            AddEnhancement(enhancements, KodiEnhancerTarget.IsDefaultVideoVersion, context.Movie.IsDefaultVideoVersion);
            AddEnhancement(enhancements, KodiEnhancerTarget.Credits, context.Movie.Credits);
            AddEnhancement(enhancements, KodiEnhancerTarget.Director, context.Movie.Director);
            AddEnhancement(enhancements, KodiEnhancerTarget.Premiered, context.Movie.Premiered);
            AddEnhancement(enhancements, KodiEnhancerTarget.Year, context.Movie.Year);
            AddEnhancement(enhancements, KodiEnhancerTarget.Status, context.Movie.Status);
            AddEnhancement(enhancements, KodiEnhancerTarget.Studio, context.Movie.Studio);
            AddEnhancement(enhancements, KodiEnhancerTarget.Trailer, context.Movie.Trailer);
            AddEnhancement(enhancements, KodiEnhancerTarget.Top250, context.Movie.Top250);
            AddEnhancement(enhancements, KodiEnhancerTarget.UserRating, context.Movie.UserRating?.Value);
            AddEnhancement(enhancements, KodiEnhancerTarget.Thumbs, context.Movie.Thumbs?.Select(t => t.Url).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.FanartThumbs, context.Movie.FanartThumbs?.Select(t => t.Url).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.UniqueIds, context.Movie.UniqueIds?.Select(u => u.Value).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.Actors, context.Movie.Actors?.Select(a => a.Name).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.Ratings, context.Movie.Ratings?.Select(r => r.Name).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.Set, context.Movie.Set?.Name);
            AddEnhancement(enhancements, KodiEnhancerTarget.FanartThumbs, context.Movie.FanartThumbs?.Select(t => t.Url).ToList());
        }

        // Process TvShow data
        if (context.TvShow != null)
        {
            AddEnhancement(enhancements, KodiEnhancerTarget.Title, context.TvShow.Title);
            AddEnhancement(enhancements, KodiEnhancerTarget.OriginalTitle, context.TvShow.OriginalTitle);
            AddEnhancement(enhancements, KodiEnhancerTarget.ShowTitle, context.TvShow.ShowTitle);
            AddEnhancement(enhancements, KodiEnhancerTarget.Outline, context.TvShow.Outline);
            AddEnhancement(enhancements, KodiEnhancerTarget.Plot, context.TvShow.Plot);
            AddEnhancement(enhancements, KodiEnhancerTarget.Tagline, context.TvShow.Tagline);
            AddEnhancement(enhancements, KodiEnhancerTarget.Runtime, context.TvShow.Runtime);
            AddEnhancement(enhancements, KodiEnhancerTarget.Mpaa, context.TvShow.Mpaa);
            AddEnhancement(enhancements, KodiEnhancerTarget.PlayCount, context.TvShow.PlayCount);
            AddEnhancement(enhancements, KodiEnhancerTarget.LastPlayed, context.TvShow.LastPlayed);
            AddEnhancement(enhancements, KodiEnhancerTarget.Id, context.TvShow.Id);
            AddEnhancement(enhancements, KodiEnhancerTarget.Genre, context.TvShow.Genre);
            AddEnhancement(enhancements, KodiEnhancerTarget.Premiered, context.TvShow.Premiered);
            AddEnhancement(enhancements, KodiEnhancerTarget.Year, context.TvShow.Year);
            AddEnhancement(enhancements, KodiEnhancerTarget.Status, context.TvShow.Status);
            AddEnhancement(enhancements, KodiEnhancerTarget.Code, context.TvShow.Code);
            AddEnhancement(enhancements, KodiEnhancerTarget.Aired, context.TvShow.Aired);
            AddEnhancement(enhancements, KodiEnhancerTarget.Studio, context.TvShow.Studio);
            AddEnhancement(enhancements, KodiEnhancerTarget.Trailer, context.TvShow.Trailer);
            AddEnhancement(enhancements, KodiEnhancerTarget.Season, context.TvShow.Season);
            AddEnhancement(enhancements, KodiEnhancerTarget.Episode, context.TvShow.Episode);
            AddEnhancement(enhancements, KodiEnhancerTarget.DisplaySeason, context.TvShow.DisplaySeason);
            AddEnhancement(enhancements, KodiEnhancerTarget.DisplayEpisode, context.TvShow.DisplayEpisode);
            AddEnhancement(enhancements, KodiEnhancerTarget.DateAdded, context.TvShow.DateAdded);
            AddEnhancement(enhancements, KodiEnhancerTarget.Top250, context.TvShow.Top250);
            AddEnhancement(enhancements, KodiEnhancerTarget.UserRating, context.TvShow.UserRating?.Value);
            AddEnhancement(enhancements, KodiEnhancerTarget.Thumbs, context.TvShow.Thumbs?.Select(t => t.Url).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.FanartThumbs, context.TvShow.FanartThumbs?.Select(t => t.Url).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.UniqueIds, context.TvShow.UniqueIds?.Select(u => u.Value).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.Actors, context.TvShow.Actors?.Select(a => a.Name).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.NamedSeasons, context.TvShow.NamedSeasons?.Select(ns => ns.Name).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.Ratings, context.TvShow.Ratings?.Select(r => r.Name).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.Resume, context.TvShow.Resume?.Position?.ToString());
            AddEnhancement(enhancements, KodiEnhancerTarget.FanartThumbs, context.TvShow.FanartThumbs?.Select(t => t.Url).ToList());
        }

        // Process MusicVideo data
        if (context.MusicVideo != null)
        {
            AddEnhancement(enhancements, KodiEnhancerTarget.Title, context.MusicVideo.Title);
            AddEnhancement(enhancements, KodiEnhancerTarget.Album, context.MusicVideo.Album);
            AddEnhancement(enhancements, KodiEnhancerTarget.Artist, context.MusicVideo.Artist);
            AddEnhancement(enhancements, KodiEnhancerTarget.Outline, context.MusicVideo.Outline);
            AddEnhancement(enhancements, KodiEnhancerTarget.Plot, context.MusicVideo.Plot);
            AddEnhancement(enhancements, KodiEnhancerTarget.Tagline, context.MusicVideo.Tagline);
            AddEnhancement(enhancements, KodiEnhancerTarget.Runtime, context.MusicVideo.Runtime);
            AddEnhancement(enhancements, KodiEnhancerTarget.Mpaa, context.MusicVideo.Mpaa);
            AddEnhancement(enhancements, KodiEnhancerTarget.PlayCount, context.MusicVideo.PlayCount);
            AddEnhancement(enhancements, KodiEnhancerTarget.LastPlayed, context.MusicVideo.LastPlayed);
            AddEnhancement(enhancements, KodiEnhancerTarget.Id, context.MusicVideo.Id);
            AddEnhancement(enhancements, KodiEnhancerTarget.Genre, context.MusicVideo.Genre);
            AddEnhancement(enhancements, KodiEnhancerTarget.Year, context.MusicVideo.Year);
            AddEnhancement(enhancements, KodiEnhancerTarget.Status, context.MusicVideo.Status);
            AddEnhancement(enhancements, KodiEnhancerTarget.Code, context.MusicVideo.Code);
            AddEnhancement(enhancements, KodiEnhancerTarget.Aired, context.MusicVideo.Aired);
            AddEnhancement(enhancements, KodiEnhancerTarget.Trailer, context.MusicVideo.Trailer);
            AddEnhancement(enhancements, KodiEnhancerTarget.Top250, context.MusicVideo.Top250);
            AddEnhancement(enhancements, KodiEnhancerTarget.UserRating, context.MusicVideo.UserRating?.Value);
            AddEnhancement(enhancements, KodiEnhancerTarget.Thumbs, context.MusicVideo.Thumbs?.Select(t => t.Url).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.Directors, context.MusicVideo.Directors);
            AddEnhancement(enhancements, KodiEnhancerTarget.Track, context.MusicVideo.Track);
        }

        // Process Album data
        if (context.Album != null)
        {
            AddEnhancement(enhancements, KodiEnhancerTarget.Album, context.Album.Title);
            AddEnhancement(enhancements, KodiEnhancerTarget.Artist, context.Album.ArtistDesc);
            AddEnhancement(enhancements, KodiEnhancerTarget.Genre, context.Album.Genre);
            AddEnhancement(enhancements, KodiEnhancerTarget.Style, context.Album.Style);
            AddEnhancement(enhancements, KodiEnhancerTarget.Mood, context.Album.Mood);
            AddEnhancement(enhancements, KodiEnhancerTarget.Themes, context.Album.Themes);
            AddEnhancement(enhancements, KodiEnhancerTarget.Compilation, context.Album.Compilation);
            AddEnhancement(enhancements, KodiEnhancerTarget.BoxSet, context.Album.BoxSet);
            AddEnhancement(enhancements, KodiEnhancerTarget.Review, context.Album.Review);
            AddEnhancement(enhancements, KodiEnhancerTarget.Type, context.Album.Type);
            AddEnhancement(enhancements, KodiEnhancerTarget.ReleaseStatus, context.Album.ReleaseStatus);
            AddEnhancement(enhancements, KodiEnhancerTarget.ReleaseDate, context.Album.ReleaseDate);
            AddEnhancement(enhancements, KodiEnhancerTarget.OriginalReleaseDate, context.Album.OriginalReleaseDate);
            AddEnhancement(enhancements, KodiEnhancerTarget.Label, context.Album.Label);
            AddEnhancement(enhancements, KodiEnhancerTarget.Duration, context.Album.Duration);
            AddEnhancement(enhancements, KodiEnhancerTarget.Path, context.Album.Path);
            AddEnhancement(enhancements, KodiEnhancerTarget.Votes, context.Album.Votes);
            AddEnhancement(enhancements, KodiEnhancerTarget.ReleaseType, context.Album.ReleaseType);
            AddEnhancement(enhancements, KodiEnhancerTarget.Thumbs, context.Album.Thumbs?.Select(t => t.Url).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.UserRating, context.Album.UserRating?.Value);
            AddEnhancement(enhancements, KodiEnhancerTarget.Rating, context.Album.Rating?.Value);
            AddEnhancement(enhancements, KodiEnhancerTarget.AlbumArtistCredits, context.Album.AlbumArtistCredits?.Artist);
        }

        // Process Artist data
        if (context.Artist != null)
        {
            AddEnhancement(enhancements, KodiEnhancerTarget.Artist, context.Artist.Name);
            AddEnhancement(enhancements, KodiEnhancerTarget.SortName, context.Artist.SortName);
            AddEnhancement(enhancements, KodiEnhancerTarget.Type, context.Artist.Type);
            AddEnhancement(enhancements, KodiEnhancerTarget.Gender, context.Artist.Gender);
            AddEnhancement(enhancements, KodiEnhancerTarget.Disambiguation, context.Artist.Disambiguation);
            AddEnhancement(enhancements, KodiEnhancerTarget.Genre, context.Artist.Genre);
            AddEnhancement(enhancements, KodiEnhancerTarget.Styles, context.Artist.Styles);
            AddEnhancement(enhancements, KodiEnhancerTarget.Moods, context.Artist.Moods);
            AddEnhancement(enhancements, KodiEnhancerTarget.YearsActive, context.Artist.YearsActive);
            AddEnhancement(enhancements, KodiEnhancerTarget.Born, context.Artist.Born);
            AddEnhancement(enhancements, KodiEnhancerTarget.Formed, context.Artist.Formed);
            AddEnhancement(enhancements, KodiEnhancerTarget.Biography, context.Artist.Biography);
            AddEnhancement(enhancements, KodiEnhancerTarget.Died, context.Artist.Died);
            AddEnhancement(enhancements, KodiEnhancerTarget.Disbanded, context.Artist.Disbanded);
            AddEnhancement(enhancements, KodiEnhancerTarget.Path, context.Artist.Path);
            AddEnhancement(enhancements, KodiEnhancerTarget.Thumbs, context.Artist.Thumbs?.Select(t => t.Url).ToList());
        }

        // Process Episodes data
        if (context.Episodes != null && context.Episodes.Any())
        {
            var episodeTitles = context.Episodes.Select(e => e.Title).Where(t => !string.IsNullOrEmpty(t)).ToList();
            if (episodeTitles.Any())
            {
                AddEnhancement(enhancements, KodiEnhancerTarget.Episodes, episodeTitles);
            }

            // Extract additional episode information from the first episode (assuming they're all from the same show)
            var firstEpisode = context.Episodes.First();
            AddEnhancement(enhancements, KodiEnhancerTarget.ShowTitle, firstEpisode.ShowTitle);
            AddEnhancement(enhancements, KodiEnhancerTarget.Outline, firstEpisode.Outline);
            AddEnhancement(enhancements, KodiEnhancerTarget.Plot, firstEpisode.Plot);
            AddEnhancement(enhancements, KodiEnhancerTarget.Tagline, firstEpisode.Tagline);
            AddEnhancement(enhancements, KodiEnhancerTarget.Runtime, firstEpisode.Runtime);
            AddEnhancement(enhancements, KodiEnhancerTarget.Mpaa, firstEpisode.Mpaa);
            AddEnhancement(enhancements, KodiEnhancerTarget.PlayCount, firstEpisode.PlayCount);
            AddEnhancement(enhancements, KodiEnhancerTarget.LastPlayed, firstEpisode.LastPlayed);
            AddEnhancement(enhancements, KodiEnhancerTarget.Id, firstEpisode.Id);
            AddEnhancement(enhancements, KodiEnhancerTarget.Genre, firstEpisode.Genre);
            AddEnhancement(enhancements, KodiEnhancerTarget.Credits, firstEpisode.Credits);
            AddEnhancement(enhancements, KodiEnhancerTarget.Director, firstEpisode.Director);
            AddEnhancement(enhancements, KodiEnhancerTarget.Premiered, firstEpisode.Premiered);
            AddEnhancement(enhancements, KodiEnhancerTarget.Year, firstEpisode.Year);
            AddEnhancement(enhancements, KodiEnhancerTarget.Status, firstEpisode.Status);
            AddEnhancement(enhancements, KodiEnhancerTarget.Code, firstEpisode.Code);
            AddEnhancement(enhancements, KodiEnhancerTarget.Aired, firstEpisode.Aired);
            AddEnhancement(enhancements, KodiEnhancerTarget.Studio, firstEpisode.Studio);
            AddEnhancement(enhancements, KodiEnhancerTarget.Trailer, firstEpisode.Trailer);
            AddEnhancement(enhancements, KodiEnhancerTarget.Top250, firstEpisode.Top250);
            AddEnhancement(enhancements, KodiEnhancerTarget.UserRating, firstEpisode.UserRating?.Value);
            AddEnhancement(enhancements, KodiEnhancerTarget.Thumbs, firstEpisode.Thumbs?.Select(t => t.Url).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.UniqueIds, firstEpisode.UniqueIds?.Select(u => u.Value).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.Actors, firstEpisode.Actors?.Select(a => a.Name).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.Ratings, firstEpisode.Ratings?.Select(r => r.Name).ToList());
            AddEnhancement(enhancements, KodiEnhancerTarget.Resume, firstEpisode.Resume?.Position?.ToString());
        }

        return enhancements;
    }

    private void AddEnhancement<T>(List<EnhancementTargetValue<KodiEnhancerTarget>> enhancements, 
        KodiEnhancerTarget target, T? value)
    {
        if (value == null) return;

        IStandardValueBuilder? valueBuilder = null;
        
        if (value is string strValue && !string.IsNullOrEmpty(strValue))
        {
            valueBuilder = new StringValueBuilder(strValue);
        }
        else if (value is int intValue)
        {
            valueBuilder = new DecimalValueBuilder(intValue);
        }
        else if (value is decimal decValue)
        {
            valueBuilder = new DecimalValueBuilder(decValue);
        }
        else if (value is bool boolValue)
        {
            valueBuilder = new BooleanValueBuilder(boolValue);
        }
        else if (value is List<string> listValue && listValue.Any())
        {
            valueBuilder = new ListStringValueBuilder(listValue);
        }

        if (valueBuilder?.Value != null)
        {
            enhancements.Add(new EnhancementTargetValue<KodiEnhancerTarget>(target, null, valueBuilder));
        }
    }
}