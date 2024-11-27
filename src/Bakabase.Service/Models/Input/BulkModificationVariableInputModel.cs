using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Service.Models.Input;

public record BulkModificationVariableInputModel
{
    public PropertyValueScope Scope { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public string Name { get; set; } = null!;
    public string? Preprocesses { get; set; }
}