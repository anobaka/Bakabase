using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Models.View;

namespace Bakabase.Modules.BulkModification.Components.Processors.String;

public record BulkModificationStringProcessOptionsViewModel
{
    public BulkModificationProcessValueViewModel? Value { get; set; }
    public string? Find { get; set; }
    public int? Index { get; set; }
    public bool? IsPositioningDirectionReversed { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
    public int? Count { get; set; }
}