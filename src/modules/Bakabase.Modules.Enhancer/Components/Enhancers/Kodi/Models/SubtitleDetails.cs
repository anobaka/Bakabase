using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

public record SubtitleDetails
{
    [XmlElement("language", IsNullable = true)]
    public string? Language { get; set; }
}