using Bakabase.Modules.BulkModification.Components.Processors.String;
using Bakabase.Modules.BulkModification.Models.View;

namespace Bakabase.Modules.BulkModification.Components.Processors.Link;

public record BulkModificationLinkProcessOptionsViewModel
{
    public BulkModificationProcessValueViewModel? Value { get; set; }
    public BulkModificationProcessValueViewModel? Text { get; set; }
    public BulkModificationProcessValueViewModel? Url { get; set; }
    public BulkModificationStringProcessOperation? StringOperation { get; set; }
    public BulkModificationStringProcessOptionsViewModel? StringOptions { get; set; }
}
