using Bakabase.Abstractions.Components.TextProcessing;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.BulkModification.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Components.Processors.String;

public record
    BulkModificationStringProcessOptions : AbstractBulkModificationProcessOptions<
    BulkModificationStringProcessorOptions, BulkModificationStringProcessOptionsViewModel>
{
    public BulkModificationProcessValue? Value { get; set; }
    public string? Find { get; set; }
    public int? Index { get; set; }
    public bool? IsPositioningDirectionReversed { get; set; }
    public bool? IsOperationDirectionReversed { get; set; }
    public int? Count { get; set; }

    public override void PopulateData(PropertyMap? propertyMap)
    {
        Value?.PopulateData(propertyMap);
    }

    protected override BulkModificationStringProcessOptionsViewModel? ToViewModelInternal(
        IPropertyLocalizer? propertyLocalizer)
    {
        return new BulkModificationStringProcessOptionsViewModel
        {
            Count = Count,
            Find = Find,
            Index = Index,
            IsOperationDirectionReversed = IsOperationDirectionReversed,
            IsPositioningDirectionReversed = IsPositioningDirectionReversed,
            Value = Value?.ToViewModel(propertyLocalizer)
        };
    }

    protected override BulkModificationStringProcessorOptions ConvertToProcessorOptionsInternal(
        Dictionary<string, (StandardValueType Type, object? Value)>? variableMap,
        Dictionary<PropertyPool, Dictionary<int, Bakabase.Abstractions.Models.Domain.Property>>? propertyMap,
        IBulkModificationLocalizer localizer)
    {
        var options = new BulkModificationStringProcessorOptions
        {
            Find = Find,
            Index = Index,
            IsPositioningDirectionReversed = IsPositioningDirectionReversed,
            IsOperationDirectionReversed = IsOperationDirectionReversed,
            Count = Count,
            Value = Value?.ConvertToStdValue<string>(StandardValueType.String, variableMap, localizer)
        };
        return options;
    }
}