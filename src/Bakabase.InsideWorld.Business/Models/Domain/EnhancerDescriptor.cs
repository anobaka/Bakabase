using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Domain;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public record EnhancerDescriptor : ComponentDescriptor
    {
        public string[] Targets { get; set; }

        public override ComponentDescriptor ToDto()
        {
            var dto = base.ToDto();
            return new EnhancerDescriptor
            {
                AssemblyQualifiedTypeName = dto.AssemblyQualifiedTypeName,
                ComponentType = dto.ComponentType,
                OptionsId = dto.OptionsId,
                Description = dto.Description,
                Name = dto.Name,
                DataVersion = dto.DataVersion,
                Message = dto.Message,
                OptionsJson = dto.OptionsJson,
                OptionsType = dto.OptionsType,
                Type = dto.Type,
                Version = dto.Version,
                Targets = Targets
            };
        }
    }
}