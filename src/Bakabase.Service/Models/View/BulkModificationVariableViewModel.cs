using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using System.Collections.Generic;
using Bakabase.Modules.BulkModification.Abstractions.Models;

namespace Bakabase.Service.Models.View;

public record BulkModificationVariableViewModel
{
    public PropertyValueScope Scope { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public PropertyViewModel Property { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public List<BulkModificationProcessStep>? Preprocesses { get; set; }
}