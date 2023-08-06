using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class AliasCreateRequestModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;
        public int? GroupId { get; set; }
    }
}
