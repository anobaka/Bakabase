using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

public record NamedSeason
{
    [XmlAttribute("number")]
    public string? Number { get; set; }

    [XmlText]
    public string? Name { get; set; }
}