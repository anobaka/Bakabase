using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Components.Processors.String;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.BulkModification.Components.Processors.Link;

public record BulkModificationLinkProcessorOptions : IBulkModificationProcessorOptions
{
    public LinkValue? Value { get; set; }
    public string? Text { get; set; }
    public string? Url { get; set; }
    public BulkModificationStringProcessOperation? StringOperation { get; set; }
    public BulkModificationStringProcessorOptions? StringOptions { get; set; }
}
