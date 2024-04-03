using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Nfo.Models;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.Resource.Nfo.Serializers.Infrastructures
{
    internal abstract class AbstractResourceNfoSerializer<TNfo> : IResourceNfoSerializer where TNfo: VersionNfo
    {
        public abstract int Version { get; }

        public string Serialize(Business.Models.Domain.Resource resource)
        {
            var nfo = ToNfo(resource);
            nfo.Version = Version;
            return SerializeInternal(nfo);
        }

        public Business.Models.Domain.Resource Deserialize(string xml)
        {
            var nfo = DeserializeInternal(xml);
            return ToResource(nfo);
        }

        protected abstract string SerializeInternal(TNfo nfo);
        protected abstract TNfo DeserializeInternal(string xml);
        protected abstract Business.Models.Domain.Resource ToResource(TNfo nfo);
        protected abstract TNfo ToNfo(Business.Models.Domain.Resource resource);
    }
}