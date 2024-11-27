using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Abstractions.Models;

public record BulkModificationVariable : IPropertyKeyHolder
{
    public PropertyValueScope Scope { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public Bakabase.Abstractions.Models.Domain.Property Property { get; set; } = null!;
    /// <summary>
    /// Auto generated
    /// </summary>
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;

    public List<BulkModificationProcessStep>? Preprocesses { get; set; }
}