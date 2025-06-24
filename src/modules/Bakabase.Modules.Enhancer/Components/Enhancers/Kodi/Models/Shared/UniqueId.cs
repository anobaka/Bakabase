using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;

public record UniqueId
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlAttribute("default")]
    public bool? Default { get; set; }

    [XmlText]
    public string? Value { get; set; }
}