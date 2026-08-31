using System.ComponentModel.DataAnnotations;

namespace Bakabase.Service.Models.Input;

public record ResourceMoveInputModel
{
    [Required] public int[] ResourceIds { get; set; } = [];
    [Required] public string DestDir { get; set; } = null!;
}
