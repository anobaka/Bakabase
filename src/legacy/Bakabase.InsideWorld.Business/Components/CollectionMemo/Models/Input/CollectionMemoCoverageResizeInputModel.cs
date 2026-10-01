using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Input;

public class CollectionMemoCoverageResizeInputModel
{
    /// <summary>The complete, unchanged raw records of the connected coverage being resized.</summary>
    [Required]
    [MinLength(1)]
    public List<CollectionMemoRangeSnapshotInputModel> Ranges { get; set; } = [];

    [Required]
    [RegularExpression("^(start|end)$")]
    public string Edge { get; set; } = null!;

    /// <summary>New boundary as an ISO 8601 date-time with Z or an explicit UTC offset.</summary>
    [Required]
    public string At { get; set; } = null!;
}

public class CollectionMemoRangeSnapshotInputModel : CollectionMemoRangeInputModel
{
    public int Id { get; set; }
}
