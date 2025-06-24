using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;

public record Actor
{
    [XmlElement("name", IsNullable = true)]
    public string? Name { get; set; }

    [XmlElement("role", IsNullable = true)]
    public string? Role { get; set; }

    [XmlElement("order", IsNullable = true)]
    public int? Order { get; set; }

    [XmlElement("thumb", IsNullable = true)]
    public string? Thumb { get; set; }
}