using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;

public record Rating
{
    [XmlAttribute("name")]
    public string? Name { get; set; }

    [XmlAttribute("max")] public int? Max { get; set; }

    [XmlAttribute("default")]
    public bool? Default { get; set; }

    [XmlElement("value", IsNullable = true)]
    public double? Value { get; set; }

    [XmlElement("votes", IsNullable = true)]
    public int? Votes { get; set; }
}