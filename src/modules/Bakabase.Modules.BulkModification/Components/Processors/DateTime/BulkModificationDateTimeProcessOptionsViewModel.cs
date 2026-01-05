using Bakabase.Modules.BulkModification.Models.View;

namespace Bakabase.Modules.BulkModification.Components.Processors.DateTime;

public record BulkModificationDateTimeProcessOptionsViewModel
{
    public BulkModificationProcessValueViewModel? Value { get; set; }
    public int? Amount { get; set; }
}
