using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

public record AudioDetails
{
    [XmlElement("codec", IsNullable = true)]
    public string? Codec { get; set; }

    [XmlElement("language", IsNullable = true)]
    public string? Language { get; set; }

    [XmlElement("channels", IsNullable = true)]
    public int? Channels { get; set; }
}