using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components.Processors.Time;

public record BulkModificationTimeProcessorOptions : IBulkModificationProcessorOptions
{
    public TimeSpan? Value { get; set; }
    public int? Amount { get; set; }
}
