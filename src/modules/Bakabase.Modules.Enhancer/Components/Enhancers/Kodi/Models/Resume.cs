using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models;

public record Resume
{
    [XmlElement("position", IsNullable = true)]
    public double? Position { get; set; }

    [XmlElement("total", IsNullable = true)]
    public double? Total { get; set; }
}