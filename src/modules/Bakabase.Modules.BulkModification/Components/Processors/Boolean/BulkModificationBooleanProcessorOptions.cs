using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components.Processors.Boolean;

public record BulkModificationBooleanProcessorOptions : IBulkModificationProcessorOptions
{
    public bool? Value { get; set; }
}
