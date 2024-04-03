using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.Resource.Nfo.Serializers.Infrastructures
{
    internal interface IResourceNfoSerializer
    {
        int Version { get; }
        [Obsolete("Decoupling with resource")]
        Business.Models.Domain.Resource Deserialize(string xml);
        string Serialize(Business.Models.Domain.Resource resource);
    }
}
