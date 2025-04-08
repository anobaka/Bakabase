using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

public record FanartThumb
{
    [XmlAttribute("colors")]
    public string? Colors { get; set; }

    [XmlAttribute("preview")]
    public string? Preview { get; set; }

    [XmlText]
    public string? Url { get; set; }
}
