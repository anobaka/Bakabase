using Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;
using System.Collections.Generic;
using System.Xml.Serialization;
using FileInfo = Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared.FileInfo;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

[XmlRoot("movie")]
public record Movie
{
    [XmlElement("title", IsNullable = true)]
    public string? Title { get; set; }

    [XmlElement("originaltitle", IsNullable = true)]
    public string? OriginalTitle { get; set; }

    [XmlElement("sorttitle", IsNullable = true)]
    public string? SortTitle { get; set; }

    [XmlArray("ratings")]
    [XmlArrayItem("rating")]
    public List<Rating>? Ratings { get; set; }

    [XmlElement("userrating", IsNullable = true)]
    public Rating? UserRating { get; set; }

    [XmlElement("top250", IsNullable = true)]
    public int? Top250 { get; set; }

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

    [XmlArray("fanart")]
    [XmlArrayItem("thumb")]
    public List<FanartThumb>? FanartThumbs { get; set; }

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

    [XmlArray("genre")]
    [XmlArrayItem("genre")]
    public List<string>? Genres { get; set; }

    [XmlArray("country")]
    [XmlArrayItem("country")]
    public List<string>? Countries { get; set; }

    [XmlElement("set", IsNullable = true)]
    public MovieSet? Set { get; set; }

    [XmlArray("tag")]
    [XmlArrayItem("tag")]
    public List<string>? Tags { get; set; }

    [XmlElement("videoassettitle", IsNullable = true)]
    public string? VideoAssetTitle { get; set; }

    [XmlElement("videoassetid", IsNullable = true)]
    public string? VideoAssetId { get; set; }

    [XmlElement("videoassettype", IsNullable = true)]
    public int? VideoAssetType { get; set; }

    [XmlElement("hasvideoversions", IsNullable = true)]
    public bool? HasVideoVersions { get; set; }

    [XmlElement("hasvideoextras", IsNullable = true)]
    public bool? HasVideoExtras { get; set; }

    [XmlElement("isdefaultvideoversion", IsNullable = true)]
    public bool? IsDefaultVideoVersion { get; set; }

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

    [XmlElement("studio", IsNullable = true)]
    public string? Studio { get; set; }

    [XmlElement("trailer", IsNullable = true)]
    public string? Trailer { get; set; }

    [XmlElement("fileinfo", IsNullable = true)]
    public FileInfo? FileInfo { get; set; }

    [XmlArray("actor")]
    [XmlArrayItem("actor")]
    public List<Actor>? Actors { get; set; }
}