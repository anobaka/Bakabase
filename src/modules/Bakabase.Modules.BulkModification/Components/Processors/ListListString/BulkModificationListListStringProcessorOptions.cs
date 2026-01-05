using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListListString;

public record BulkModificationListListStringProcessorOptions : IBulkModificationProcessorOptions
{
    public List<List<string>>? Value { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
}
