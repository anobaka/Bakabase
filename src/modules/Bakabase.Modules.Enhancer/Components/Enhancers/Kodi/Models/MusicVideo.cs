using Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;
using FileInfo = Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared.FileInfo;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

using System.Collections.Generic;
using System.Xml.Serialization;

[XmlRoot("musicvideo")]
public record MusicVideo
{
    [XmlElement("title", IsNullable = true)]
    public string? Title { get; set; }

    [XmlElement("userrating", IsNullable = true)]
    public Rating? UserRating { get; set; }

    [XmlElement("top250", IsNullable = true)]
    public int? Top250 { get; set; }

    [XmlElement("track", IsNullable = true)]
    public int? Track { get; set; }

    [XmlElement("album", IsNullable = true)]
    public string? Album { get; set; }

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

    [XmlElement("genre", IsNullable = true)]
    public string? Genre { get; set; }

    [XmlElement("year", IsNullable = true)]
    public int? Year { get; set; }

    [XmlElement("status", IsNullable = true)]
    public string? Status { get; set; }

    [XmlArray("directors")]
    [XmlArrayItem("director")]
    public List<string>? Directors { get; set; }

    [XmlElement("code", IsNullable = true)]
    public string? Code { get; set; }

    [XmlElement("aired", IsNullable = true)]
    public string? Aired { get; set; }

    [XmlElement("trailer", IsNullable = true)]
    public string? Trailer { get; set; }

    [XmlElement("fileinfo", IsNullable = true)]
    public FileInfo? FileInfo { get; set; }

    [XmlElement("artist", IsNullable = true)]
    public string? Artist { get; set; }

    [XmlElement("resume", IsNullable = true)]
    public Resume? Resume { get; set; }

    [XmlElement("dateadded", IsNullable = true)]
    public string? DateAdded { get; set; }
}