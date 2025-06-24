using Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;
using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

[XmlRoot("album")]
public record Album
{
    [XmlElement("title", IsNullable = true)]
    public string? Title { get; set; }

    [XmlElement("musicbrainzalbumid", IsNullable = true)]
    public string? MusicBrainzAlbumId { get; set; }

    [XmlElement("musicbrainzreleasegroupid", IsNullable = true)]
    public string? MusicBrainzReleaseGroupId { get; set; }

    [XmlElement("scrapedmbid", IsNullable = true)]
    public bool? ScrapedMbid { get; set; }

    [XmlElement("artistdesc", IsNullable = true)]
    public string? ArtistDesc { get; set; }

    [XmlElement("genre", IsNullable = true)]
    public string? Genre { get; set; }

    [XmlElement("style", IsNullable = true)]
    public string? Style { get; set; }

    [XmlElement("mood", IsNullable = true)]
    public string? Mood { get; set; }

    [XmlArray("themes")]
    [XmlArrayItem("theme")]
    public List<string>? Themes { get; set; }

    [XmlElement("compilation", IsNullable = true)]
    public bool? Compilation { get; set; }

    [XmlElement("boxset", IsNullable = true)]
    public bool? BoxSet { get; set; }

    [XmlElement("review", IsNullable = true)]
    public string? Review { get; set; }

    [XmlElement("type", IsNullable = true)]
    public string? Type { get; set; }

    [XmlElement("releasestatus", IsNullable = true)]
    public string? ReleaseStatus { get; set; }

    [XmlElement("releasedate", IsNullable = true)]
    public string? ReleaseDate { get; set; }

    [XmlElement("originalreleasedate", IsNullable = true)]
    public string? OriginalReleaseDate { get; set; }

    [XmlElement("label", IsNullable = true)]
    public string? Label { get; set; }

    [XmlElement("duration", IsNullable = true)]
    public int? Duration { get; set; }

    [XmlArray("thumbs")]
    [XmlArrayItem("thumb")]
    public List<Thumb>? Thumbs { get; set; }

    [XmlElement("path", IsNullable = true)]
    public string? Path { get; set; }

    [XmlElement("rating", IsNullable = true)]
    public Rating? Rating { get; set; }

    [XmlElement("userrating", IsNullable = true)]
    public Rating? UserRating { get; set; }

    [XmlElement("votes", IsNullable = true)]
    public int? Votes { get; set; }

    [XmlElement("albumArtistCredits", IsNullable = true)]
    public AlbumArtistCredits? AlbumArtistCredits { get; set; }

    [XmlElement("releasetype", IsNullable = true)]
    public string? ReleaseType { get; set; }
}