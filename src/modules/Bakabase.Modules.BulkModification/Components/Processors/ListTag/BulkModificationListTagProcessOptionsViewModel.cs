using Bakabase.Modules.BulkModification.Models.View;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListTag;

public record BulkModificationListTagProcessOptionsViewModel
{
    public BulkModificationProcessValueViewModel? Value { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
}
