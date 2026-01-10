using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components.Processors.Decimal;

public record BulkModificationDecimalProcessorOptions : IBulkModificationProcessorOptions
{
    public decimal? Value { get; set; }
    public int? DecimalPlaces { get; set; }
}
