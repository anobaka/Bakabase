using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class TagUpdateRequestModel
    {
        public string? Color { get; set; }
        public int? GroupId { get; set; }
        public int? Order { get; set; }
    }
}