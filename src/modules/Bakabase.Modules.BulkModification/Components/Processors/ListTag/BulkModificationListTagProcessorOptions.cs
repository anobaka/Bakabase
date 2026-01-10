using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListTag;

public record BulkModificationListTagProcessorOptions : IBulkModificationProcessorOptions
{
    public List<TagValue>? Value { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
}
