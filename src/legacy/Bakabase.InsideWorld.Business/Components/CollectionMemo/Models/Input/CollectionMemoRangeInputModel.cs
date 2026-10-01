using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Input;

public class CollectionMemoRangeInputModel
{
    /// <summary>ISO 8601 date-time with Z or an explicit UTC offset.</summary>
    [Required]
    public string StartAt { get; set; } = null!;

    /// <summary>ISO 8601 date-time with Z or an explicit UTC offset; equal to StartAt for a point.</summary>
    [Required]
    public string EndAt { get; set; } = null!;
}
