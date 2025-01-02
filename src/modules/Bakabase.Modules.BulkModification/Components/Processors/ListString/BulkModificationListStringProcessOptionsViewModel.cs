using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.BulkModification.Components.Processors.String;
using Bakabase.Modules.BulkModification.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Models.View;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Components.Processors.ListString;

public record BulkModificationListStringProcessModifyOptionsViewModel
{
    public BulkModificationProcessorOptionsItemsFilterBy FilterBy { get; set; }
    public string? FilterValue { get; set; }
    public BulkModificationStringProcessOperation Operation { get; set; }
    public BulkModificationStringProcessOptionsViewModel? Options { get; set; }
}

public record BulkModificationListStringProcessOptionsViewModel
{
    public BulkModificationProcessValueViewModel? Value { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
    public BulkModificationListStringProcessModifyOptionsViewModel? ModifyOptions { get; set; }
}