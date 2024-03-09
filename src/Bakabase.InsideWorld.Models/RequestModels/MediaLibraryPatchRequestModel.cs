using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Models.Entities;
using System.Linq;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities.Implicit;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class MediaLibraryPatchRequestModel
    {
        public string? Name { get; set; }
        public List<PathConfigurationDto>? PathConfigurations { get; set; }
        public int? Order { get; set; }
    }
}