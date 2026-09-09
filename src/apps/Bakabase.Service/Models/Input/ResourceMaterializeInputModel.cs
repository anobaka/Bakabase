using System.ComponentModel.DataAnnotations;

namespace Bakabase.Service.Models.Input;

public record ResourceMaterializeInputModel
{
    /// <summary>The folder or file the resource's files are in.</summary>
    [Required]
    public string Path { get; set; } = null!;

    /// <summary>
    /// Absorb the resource that already owns this path, if there is one. Defaults to false so the
    /// first call reports the conflict and lets the user decide — merging deletes a resource, which
    /// is not something to do on a guess.
    /// </summary>
    public bool MergeIfOccupied { get; set; }
}
