using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Input;

public class CollectionMemoSettingsInputModel
{
    /// <summary>Global timeline start as an ISO 8601 date-time with Z or an explicit UTC offset.</summary>
    [Required]
    public string StartAt { get; set; } = null!;

    /// <summary>When true, the global start is on the right and the current time is on the left.</summary>
    public bool Reverse { get; set; } = true;
}
