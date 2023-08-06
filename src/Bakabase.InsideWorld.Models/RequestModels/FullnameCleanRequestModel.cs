using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class FullnameCleanRequestModel
    {
        [Required]
        public string Fullname { get; set; }
    }
}
