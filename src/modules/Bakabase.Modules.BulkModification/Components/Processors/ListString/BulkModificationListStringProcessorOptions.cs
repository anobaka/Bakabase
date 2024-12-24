using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Components.Processors.String;
using Bakabase.Modules.BulkModification.Models.Domain.Constants;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListString;

public record BulkModificationListStringProcessorOptions : IBulkModificationProcessorOptions
{
    public List<string>? Value { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
    public BulkModificationListStringProcessorModifyOptions? ModifyOptions { get; set; }
}

public record BulkModificationListStringProcessorModifyOptions
{
    public BulkModificationProcessorOptionsItemsFilterBy FilterBy { get; set; }
    public string? FilterValue { get; set; }
    public BulkModificationStringProcessOperation Operation { get; set; }
    public BulkModificationStringProcessorOptions? Options { get; set; }
}