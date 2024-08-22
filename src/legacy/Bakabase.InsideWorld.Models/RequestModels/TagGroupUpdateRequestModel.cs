using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class TagGroupUpdateRequestModel
    {
        public string? Name { get; set; }
        public int? Order { get; set; }
    }
}