using Bakabase.Abstractions.Models.Domain.Constants;
using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Service.Models.View;

public record BulkModificationDiffViewModel
{
    public int Id { get; set; }
    public int BulkModificationId { get; set; }

    /// <summary>
    /// redundancy
    /// </summary>
    public string ResourcePath { get; set; } = null!;

    public int ResourceId { get; set; }

    public List<ResourceDiffViewModel> Diffs { get; set; } = [];
}