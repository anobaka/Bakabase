using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class FileChangesRevertRequestModel
    {
        [Required]
        public string BatchId { get; set; }
    }
}
