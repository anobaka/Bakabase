using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components.Processors.DateTime;

public record BulkModificationDateTimeProcessorOptions : IBulkModificationProcessorOptions
{
    public System.DateTime? Value { get; set; }
    public int? Amount { get; set; }
}
