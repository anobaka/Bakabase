using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Input;

public class CollectionMemoRangeInputModel
{
    /// <summary>ISO 8601 date-time with Z or an explicit UTC offset; null inherits the global timeline start dynamically.</summary>
    public string? StartAt { get; set; }

    /// <summary>ISO 8601 date-time with Z or an explicit UTC offset; equal to StartAt for a point.</summary>
    [Required]
    public string EndAt { get; set; } = null!;

    /// <summary>Optional absolute HTTP or HTTPS link without credentials.</summary>
    public string? Url { get; set; }

    /// <summary>Optional plain-text note; internal line breaks are preserved.</summary>
    public string? Note { get; set; }
}
