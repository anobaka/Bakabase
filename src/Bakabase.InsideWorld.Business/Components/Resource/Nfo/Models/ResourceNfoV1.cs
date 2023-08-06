using System.Xml.Serialization;

namespace Bakabase.InsideWorld.Business.Components.Resource.Nfo.Models
{
    /// <summary>
    /// For nfo file
    /// </summary>
    [XmlType("Resource")]
    public class ResourceNfoV1 : VersionNfo
    {
        public TagGroupNfo[] TagGroups { get; set; }
        public decimal Rate { get; set; }
        public string Name { get; set; }
        public PublisherNfo[] Publishers { get; set; }
        public string Series { get; set; }
        public string Introduction { get; set; }
        public string[] Originals { get; set; }

        [XmlType("Publisher")]
        public class PublisherNfo
        {
            public string Name { get; set; }
            public PublisherNfo[] SubPublishers { get; set; }
        }

        [XmlType("TagGroup")]
        public class TagGroupNfo
        {
            public string Name { get; set; }
            public TagNfo[] Tags { get; set; }

            [XmlType("Tag")]
            public class TagNfo
            {
                public string Name { get; set; }
            }
        }
    }
}