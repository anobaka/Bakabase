using Bakabase.Modules.BulkModification.Models.View;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListListString;

public record BulkModificationListListStringProcessOptionsViewModel
{
    public BulkModificationProcessValueViewModel? Value { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
}
