using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.InsideWorld.Business.Models.Domain
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