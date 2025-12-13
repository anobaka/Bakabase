using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.Input;

/// <summary>
/// Input model for managing resource media library mappings
/// </summary>
public class ResourceMediaLibraryMappingInputModel
{
    /// <summary>
    /// Media library IDs to associate with the resource
    /// </summary>
    public List<int> MediaLibraryIds { get; set; } = new();

    /// <summary>
    /// Source of the mapping (Manual, Rule, etc.)
    /// </summary>
    public MappingSource Source { get; set; } = MappingSource.Manual;
}
