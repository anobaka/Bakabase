using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.Modules.BulkModification.Abstractions.Models;

public record BulkModificationDiff
{
    public int Id { get; set; }
    public int BulkModificationId { get; set; }

    /// <summary>
    /// redundancy
    /// </summary>
    /// <summary>
    /// Null when the resource has no local files.
    /// </summary>
    public string? ResourcePath { get; set; }

    public int ResourceId { get; set; }

    public List<ResourceDiff> Diffs { get; set; } = [];
}