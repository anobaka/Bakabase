using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Bakabase.InsideWorld.Business.Components.Resource.Nfo.Models
{
    /// <summary>
    /// For nfo file
    /// </summary>
    [XmlType("Resource")]
    public class ResourceNfoV0 : VersionNfo
    {
        public HashSet<string> Tags { get; set; }
        [Obsolete("Use rate instead.")] public decimal Rank { get; set; }
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
    }
}