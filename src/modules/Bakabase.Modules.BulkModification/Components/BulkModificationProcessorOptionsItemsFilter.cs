using Bakabase.Modules.BulkModification.Models.Domain.Constants;

namespace Bakabase.Modules.BulkModification.Components;

public class BulkModificationProcessorOptionsItemsFilter
{
    public BulkModificationProcessorOptionsItemsFilterBy By { get; set; }
    public string? Value { get; set; }
}