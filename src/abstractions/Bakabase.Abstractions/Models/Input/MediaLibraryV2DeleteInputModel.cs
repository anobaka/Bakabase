using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Input;

public class MediaLibraryV2DeleteInputModel
{
    [Required, MinLength(1)]
    public int[] Ids { get; set; } = [];

    /// <summary>Remove selected libraries from fixed-value path marks; delete marks that have no values left.</summary>
    public bool RemoveRelatedPathMarks { get; set; }
}
