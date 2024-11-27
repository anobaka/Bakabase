using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using System.Collections.Generic;

namespace Bakabase.Service.Models.Input;

public record BulkModificationPatchInputModel
{
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
    public List<BulkModificationVariableInputModel>? Variables { get; set; }
    public ResourceSearchFilterGroupInputModel? Filter { get; set; }
    public List<BulkModificationProcessInputModel>? Processes { get; set; }
}