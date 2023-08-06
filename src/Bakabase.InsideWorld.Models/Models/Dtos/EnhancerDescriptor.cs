using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public record EnhancerDescriptor : ComponentDescriptor
    {
        public string[] Targets { get; set; }

        public override ComponentDescriptorDto ToDto()
        {
            var dto = base.ToDto();
            return new EnhancerDescriptorDto
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