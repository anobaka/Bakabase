using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

public record AlbumArtistCredits
{
    [XmlElement("artist", IsNullable = true)]
    public string? Artist { get; set; }

    [XmlElement("musicBrainzArtistID", IsNullable = true)]
    public string? MusicBrainzArtistID { get; set; }
}