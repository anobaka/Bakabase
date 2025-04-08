using Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;
using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi;

public record KodiEnhancerContext
{
    [XmlElement("album", IsNullable = true)] public Album? Album { get; set; }
    [XmlElement("artist", IsNullable = true)] public Artist? Artist { get; set; }

    [XmlArray("episodedetails")]
    [XmlArrayItem("episodedetails")]
    public List<EpisodeDetails>? Episodes { get; set; }

    [XmlElement("movie", IsNullable = true)] public Movie? Movie { get; set; }
    [XmlElement("musicvideo", IsNullable = true)] public MusicVideo? MusicVideo { get; set; }
    [XmlElement("tvshow", IsNullable = true)] public TvShow? TvShow { get; set; }
}