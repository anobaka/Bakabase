using Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;

using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

[XmlRoot("artist")]
public record Artist
{
    [XmlElement("name", IsNullable = true)]
    public string? Name { get; set; }

    [XmlElement("musicBrainzArtistID", IsNullable = true)]
    public string? MusicBrainzArtistID { get; set; }

    [XmlElement("sortname", IsNullable = true)]
    public string? SortName { get; set; }

    [XmlElement("type", IsNullable = true)]
    public string? Type { get; set; }

    [XmlElement("gender", IsNullable = true)]
    public string? Gender { get; set; }

    [XmlElement("disambiguation", IsNullable = true)]
    public string? Disambiguation { get; set; }

    [XmlElement("genre", IsNullable = true)]
    public string? Genre { get; set; }

    [XmlArray("styles")]
    [XmlArrayItem("style")]
    public List<string>? Styles { get; set; }

    [XmlArray("moods")]
    [XmlArrayItem("mood")]
    public List<string>? Moods { get; set; }

    [XmlElement("yearsactive", IsNullable = true)]
    public string? YearsActive { get; set; }

    [XmlElement("born", IsNullable = true)]
    public string? Born { get; set; }

    [XmlElement("formed", IsNullable = true)]
    public string? Formed { get; set; }

    [XmlElement("biography", IsNullable = true)]
    public string? Biography { get; set; }

    [XmlElement("died", IsNullable = true)]
    public string? Died { get; set; }

    [XmlElement("disbanded", IsNullable = true)]
    public string? Disbanded { get; set; }

    [XmlArray("thumbs")]
    [XmlArrayItem("thumb")]
    public List<Thumb>? Thumbs { get; set; }

    [XmlElement("path", IsNullable = true)]
    public string? Path { get; set; }
}

