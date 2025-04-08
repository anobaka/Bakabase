namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

using System.Collections.Generic;
using System.Xml.Serialization;

[XmlRoot("episodedetails")]
public record EpisodeDetails
{
    [XmlElement("title", IsNullable = true)]
    public string? Title { get; set; }

    [XmlElement("showtitle", IsNullable = true)]
    public string? ShowTitle { get; set; }

    [XmlArray("ratings")]
    [XmlArrayItem("rating")]
    public List<Rating>? Ratings { get; set; }

    [XmlElement("userrating", IsNullable = true)]
    public Rating? UserRating { get; set; }

    [XmlElement("top250", IsNullable = true)]
    public int? Top250 { get; set; }

    [XmlElement("season", IsNullable = true)]
    public int? Season { get; set; }

    [XmlElement("episode", IsNullable = true)]
    public int? Episode { get; set; }

    [XmlElement("displayseason", IsNullable = true)]
    public int? DisplaySeason { get; set; }

    [XmlElement("displayepisode", IsNullable = true)]
    public int? DisplayEpisode { get; set; }

    [XmlElement("outline", IsNullable = true)]
    public string? Outline { get; set; }

    [XmlElement("plot", IsNullable = true)]
    public string? Plot { get; set; }

    [XmlElement("tagline", IsNullable = true)]
    public string? Tagline { get; set; }

    [XmlElement("runtime", IsNullable = true)]
    public int? Runtime { get; set; }

    [XmlArray("thumbs")]
    [XmlArrayItem("thumb")]
    public List<Thumb>? Thumbs { get; set; }

    [XmlElement("mpaa", IsNullable = true)]
    public string? Mpaa { get; set; }

    [XmlElement("playcount", IsNullable = true)]
    public int? PlayCount { get; set; }

    [XmlElement("lastplayed", IsNullable = true)]
    public string? LastPlayed { get; set; }

    [XmlElement("id", IsNullable = true)]
    public string? Id { get; set; }

    [XmlArray("uniqueid")]
    [XmlArrayItem("uniqueid")]
    public List<UniqueId>? UniqueIds { get; set; }

    [XmlElement("genre", IsNullable = true)]
    public string? Genre { get; set; }

    [XmlArray("credits")]
    [XmlArrayItem("credits")]
    public List<string>? Credits { get; set; }

    [XmlElement("director", IsNullable = true)]
    public string? Director { get; set; }

    [XmlElement("premiered", IsNullable = true)]
    public string? Premiered { get; set; }

    [XmlElement("year", IsNullable = true)]
    public int? Year { get; set; }

    [XmlElement("status", IsNullable = true)]
    public string? Status { get; set; }

    [XmlElement("code", IsNullable = true)]
    public string? Code { get; set; }

    [XmlElement("aired", IsNullable = true)]
    public string? Aired { get; set; }

    [XmlElement("studio", IsNullable = true)]
    public string? Studio { get; set; }

    [XmlElement("trailer", IsNullable = true)]
    public string? Trailer { get; set; }

    [XmlArray("actor")]
    [XmlArrayItem("actor")]
    public List<Actor>? Actors { get; set; }

    [XmlElement("resume", IsNullable = true)]
    public Resume? Resume { get; set; }

    [XmlElement("dateadded", IsNullable = true)]
    public string? DateAdded { get; set; }
}