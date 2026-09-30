using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Federation.Contracts;

namespace Bakabase.Service.Models.Input;

public record ResourceMoveInputModel : ResourceMoveRequestOptions
{
    [Required] public int[] ResourceIds { get; set; } = [];
    [Required] public string DestDir { get; set; } = null!;

    /// <summary>The owner of every selected resource. Required for move-panel requests;
    /// legacy local-only callers may continue to send resource IDs alone.</summary>
    public ResourceRef[]? ResourceRefs { get; set; }
}
