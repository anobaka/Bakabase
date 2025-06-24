using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;

public record MovieSet
{
    [XmlElement("name", IsNullable = true)]
    public string? Name { get; set; }

    [XmlElement("overview", IsNullable = true)]
    public string? Overview { get; set; }
}