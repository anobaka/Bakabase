using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using Bakabase.InsideWorld.Business.Components.Resource.Nfo.Models;
using Bakabase.InsideWorld.Business.Components.Resource.Nfo.Serializers.Infrastructures;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Resource.Nfo.Serializers
{
    internal class ResourceNfoSerializerV0 : AbstractResourceNfoSerializer<ResourceNfoV0>
    {
        private static readonly XmlSerializer Serializer;

        static ResourceNfoSerializerV0()
        {
            var overrides = new XmlAttributeOverrides();
            var attribs = new XmlAttributes
            {
                XmlIgnore = true
            };
            attribs.XmlElements.Add(new XmlElementAttribute(nameof(ResourceNfoV0.Rank)));
            overrides.Add(SpecificTypeUtils<ResourceNfoV0>.Type, nameof(ResourceNfoV0.Rank), attribs);

            Serializer = new XmlSerializer(SpecificTypeUtils<ResourceNfoV0>.Type, overrides);
        }

        public override int Version => 0;

        protected override string SerializeInternal(ResourceNfoV0 nfo)
        {
            using var sww = new StringWriter();
            using var writer =
                XmlWriter.Create(sww, new XmlWriterSettings() {Async = true, Indent = true});
            Serializer.Serialize(writer, nfo);
            return sww.ToString();
        }

        protected override ResourceNfoV0 DeserializeInternal(string xml)
        {
            using var reader = new StringReader(xml);
            var nfo = Serializer.Deserialize(reader) as ResourceNfoV0;

            if (nfo is {Rate: 0})
            {
                nfo.Rate = nfo.Rank;
            }

            return nfo;
        }

        protected override Business.Models.Domain.Resource ToResource(ResourceNfoV0 nfo)
        {
            if (nfo == null)
            {
                return null;
            }

            return new Business.Models.Domain.Resource()
            {
                Tags = nfo.Tags?.Select(t => new TagDto {Name = t}).ToList(),
                Rate = nfo.Rate,
                Name = nfo.Name,
                Publishers = ToResource(nfo.Publishers),
                Series = nfo.Series.IsNullOrEmpty() ? null : new SeriesDto {Name = nfo.Series},
                Introduction = nfo.Introduction,
                Originals = nfo.Originals?.Select(o => new OriginalDto {Name = o}).ToList()
            };
        }

        protected override ResourceNfoV0 ToNfo(Business.Models.Domain.Resource resource)
        {
            if (resource == null)
            {
                return null;
            }

            var nfo = new ResourceNfoV0
            {
                Rate = resource.Rate,
                Tags = resource.Tags?.Any() == true ? resource.Tags.Select(a => a.Name).ToHashSet() : null,
                Introduction = resource.Introduction,
                Name = resource.Name,
                Originals = resource.Originals?.Any() == true ? resource.Originals.Select(t => t.Name).ToArray() : null,
                Publishers = resource.Publishers?.Any() == true ? resource.Publishers.Select(ToNfo).ToArray() : null,
                Series = resource.Series?.Name
            };
            return nfo;
        }

        private static PublisherDto ToResource(ResourceNfoV0.PublisherNfo nfo)
        {
            if (nfo == null)
            {
                return null;
            }

            return new PublisherDto
            {
                Name = nfo.Name,
                SubPublishers = ToResource(nfo.SubPublishers)
            };
        }

        private static List<PublisherDto> ToResource(IEnumerable<ResourceNfoV0.PublisherNfo> nfo)
        {
            return nfo?.Select(ToResource).ToList();
        }

        private static ResourceNfoV0.PublisherNfo ToNfo(PublisherDto publisher)
        {
            if (publisher == null)
            {
                return null;
            }

            return new ResourceNfoV0.PublisherNfo
            {
                SubPublishers = publisher.SubPublishers?.Any() == true
                    ? publisher.SubPublishers.Select(ToNfo).ToArray()
                    : null,
                Name = publisher.Name
            };
        }
    }
}