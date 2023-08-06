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

        public string Serialize(ResourceDto resource)
        {
            var nfo = ToNfo(resource);
            nfo.Version = Version;
            return SerializeInternal(nfo);
        }

        public ResourceDto Deserialize(string xml)
        {
            var nfo = DeserializeInternal(xml);
            return ToResource(nfo);
        }

        protected abstract string SerializeInternal(TNfo nfo);
        protected abstract TNfo DeserializeInternal(string xml);
        protected abstract ResourceDto ToResource(TNfo nfo);
        protected abstract TNfo ToNfo(ResourceDto resource);
    }
}