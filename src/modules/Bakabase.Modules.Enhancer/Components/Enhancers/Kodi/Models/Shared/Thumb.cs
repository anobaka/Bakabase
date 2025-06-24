using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;

public record Thumb
{
    [XmlAttribute("spoof")]
    public string? Spoof { get; set; }

    [XmlAttribute("cache")]
    public string? Cache { get; set; }

    [XmlAttribute("aspect")]
    public string? Aspect { get; set; }

    [XmlAttribute("preview")]
    public string? Preview { get; set; }

    [XmlText]
    public string? Url { get; set; }
}