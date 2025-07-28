using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.PlayList.Models.Input
{
    public class PlayListAddInputModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;
    }
}