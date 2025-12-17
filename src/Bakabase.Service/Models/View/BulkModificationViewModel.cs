using System;
using System.Collections.Generic;
using Bakabase.Modules.BulkModification.Abstractions.Models;

namespace Bakabase.Service.Models.View;

public record BulkModificationViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<BulkModificationVariableViewModel>? Variables { get; set; }
    public ResourceSearchViewModel? Search { get; set; }
    public List<BulkModificationProcessViewModel>? Processes { get; set; }
    public List<int>? FilteredResourceIds { get; set; }
    public DateTime? AppliedAt { get; set; }
    public int ResourceDiffCount { get; set; }
}