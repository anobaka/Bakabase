using Bakabase.Modules.BulkModification.Models.View;

namespace Bakabase.Modules.BulkModification.Components.Processors.Time;

public record BulkModificationTimeProcessOptionsViewModel
{
    public BulkModificationProcessValueViewModel? Value { get; set; }
    public int? Amount { get; set; }
}
