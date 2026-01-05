using Bakabase.Modules.BulkModification.Models.View;

namespace Bakabase.Modules.BulkModification.Components.Processors.Boolean;

public record BulkModificationBooleanProcessOptionsViewModel
{
    public BulkModificationProcessValueViewModel? Value { get; set; }
}
