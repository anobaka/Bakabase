using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class SubdirectoriesExtractRequestModel
    {
        [Required] public string Path { get; set; } = string.Empty;
    }
}
