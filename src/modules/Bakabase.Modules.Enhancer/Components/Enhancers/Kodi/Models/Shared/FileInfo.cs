using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;

public record FileInfo
{
    [XmlElement("streamdetails", IsNullable = true)]
    public StreamDetails? StreamDetails { get; set; }
}