using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Input;

public class CollectionMemoTargetInputModel
{
    [Required]
    public string Name { get; set; } = null!;
}
