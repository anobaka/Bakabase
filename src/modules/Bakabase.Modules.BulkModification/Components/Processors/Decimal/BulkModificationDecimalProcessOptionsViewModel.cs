using Bakabase.Modules.BulkModification.Models.View;

namespace Bakabase.Modules.BulkModification.Components.Processors.Decimal;

public record BulkModificationDecimalProcessOptionsViewModel
{
    public BulkModificationProcessValueViewModel? Value { get; set; }
    public int? DecimalPlaces { get; set; }
}
