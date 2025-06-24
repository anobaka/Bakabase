using System.Xml.Serialization;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi.Models.Shared;

public record VideoDetails
{
    [XmlElement("codec", IsNullable = true)]
    public string? Codec { get; set; }

    [XmlElement("aspect", IsNullable = true)]
    public double? Aspect { get; set; }

    [XmlElement("width", IsNullable = true)]
    public int? Width { get; set; }

    [XmlElement("height", IsNullable = true)]
    public int? Height { get; set; }

    [XmlElement("durationinseconds", IsNullable = true)]
    public int? DurationInSeconds { get; set; }

    [XmlElement("stereomode", IsNullable = true)]
    public string? StereoMode { get; set; }

    [XmlElement("hdrtype", IsNullable = true)]
    public string? HdrType { get; set; }
}