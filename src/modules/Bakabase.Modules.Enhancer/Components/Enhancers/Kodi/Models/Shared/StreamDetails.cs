using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;

public record StreamDetails
{
    [XmlElement("video", IsNullable = true)]
    public VideoDetails? Video { get; set; }

    [XmlElement("audio", IsNullable = true)]
    public AudioDetails? Audio { get; set; }

    [XmlElement("subtitle", IsNullable = true)]
    public SubtitleDetails? Subtitle { get; set; }
}