using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Models.Entities;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Domain;

namespace Bakabase.InsideWorld.Business.Models.Dto
{
    public class MediaLibraryPatchDto
    {
        public string? Name { get; set; }
        public List<PathConfiguration>? PathConfigurations { get; set; }
        public int? Order { get; set; }
    }
}