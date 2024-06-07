using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.Resource.Nfo.Serializers.Infrastructures
{
    internal interface IResourceNfoSerializer
    {
        int Version { get; }
        [Obsolete("Decoupling with resource")]
        Abstractions.Models.Domain.Resource Deserialize(string xml);
        string Serialize(Abstractions.Models.Domain.Resource resource);
    }
}
