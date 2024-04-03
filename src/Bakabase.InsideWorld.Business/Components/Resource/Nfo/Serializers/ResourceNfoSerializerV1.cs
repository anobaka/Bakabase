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
    internal class ResourceNfoSerializerV1 : AbstractResourceNfoSerializer<ResourceNfoV1>
    {
        private static readonly XmlSerializer Serializer;

        static ResourceNfoSerializerV1()
        {
            Serializer = new XmlSerializer(SpecificTypeUtils<ResourceNfoV1>.Type);
        }

        public override int Version => 1;

        protected override string SerializeInternal(ResourceNfoV1 nfo)
        {
            using var sww = new StringWriter();
            using var writer =
                XmlWriter.Create(sww, new XmlWriterSettings() {Async = true, Indent = true, CheckCharacters = false});
            Serializer.Serialize(writer, nfo);
            return sww.ToString();
        }

        protected override ResourceNfoV1 DeserializeInternal(string xml)
        {
            using var reader = new StringReader(xml);
            var nfo = Serializer.Deserialize(reader) as ResourceNfoV1;
            return nfo;
        }

        protected override Business.Models.Domain.Resource ToResource(ResourceNfoV1 nfo)
        {
            if (nfo == null)
            {
                return null;
            }

            return new Business.Models.Domain.Resource()
            {
                Tags = nfo.TagGroups
                    ?.SelectMany(t => t.Tags.Select(a => new TagDto {Name = a.Name, GroupName = t.Name}))
                    .ToList(),
                Rate = nfo.Rate,
                Name = nfo.Name,
                Publishers = ToResource(nfo.Publishers),
                Series = nfo.Series.IsNullOrEmpty() ? null : new SeriesDto {Name = nfo.Series},
                Introduction = nfo.Introduction,
                Originals = nfo.Originals?.Select(o => new OriginalDto {Name = o}).ToList()
            };
        }

        protected override ResourceNfoV1 ToNfo(Business.Models.Domain.Resource resource)
        {
            if (resource == null)
            {
                return null;
            }

            var nfo = new ResourceNfoV1
            {
                Rate = resource.Rate,
                TagGroups = resource.Tags?.Any() == true
                    ? resource.Tags.Distinct(TagDto.BizComparer).GroupBy(t => t.GroupName)
                        .Select(
                            a => new ResourceNfoV1.TagGroupNfo
                            {
                                Name = a.Key,
                                Tags = a.Select(b => new ResourceNfoV1.TagGroupNfo.TagNfo {Name = b.Name}).ToArray()
                            }).ToArray()
                    : null,
                Introduction = resource.Introduction.StripInvalidXmlCharacters(),
                Name = resource.Name,
                Originals = resource.Originals?.Any() == true ? resource.Originals.Select(t => t.Name).ToArray() : null,
                Publishers = resource.Publishers?.Any() == true
                    ? resource.Publishers.Select(ToNfo).ToArray()
                    : null,
                Series = resource.Series?.Name
            };
            return nfo;
        }

        private static PublisherDto ToResource(ResourceNfoV1.PublisherNfo nfo)
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

        private static List<PublisherDto> ToResource(IEnumerable<ResourceNfoV1.PublisherNfo> nfo)
        {
            return nfo?.Select(ToResource).ToList();
        }


        private static ResourceNfoV1.PublisherNfo ToNfo(PublisherDto publisher)
        {
            if (publisher == null)
            {
                return null;
            }

            return new ResourceNfoV1.PublisherNfo
            {
                SubPublishers = publisher.SubPublishers?.Any() == true
                    ? publisher.SubPublishers.Select(ToNfo).ToArray()
                    : null,
                Name = publisher.Name
            };
        }
    }
}