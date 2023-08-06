using Bakabase.InsideWorld.Models.Models.Entities;
using System.Linq;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class MediaLibraryUpdateRequestModel
    {
        public string? Name { get; set; }
        public MediaLibrary.PathConfiguration[]? PathConfigurations { get; set; }
        public int? Order { get; set; }
    }
}