using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Extensions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Property.Components.Properties.Choice;

public record SingleChoicePropertyOptions: ChoicePropertyOptions<string>;

public class SingleChoicePropertyDescriptor : AbstractPropertyDescriptor<SingleChoicePropertyOptions, string, string>
{
    public override PropertyType Type => Bakabase.Abstractions.Models.Domain.Constants.PropertyType.SingleChoice;

    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeywordInternal(
        Bakabase.Abstractions.Models.Domain.Property property, string keyword)
    {
        var options = property.Options as SingleChoicePropertyOptions;
        var ids = options?.Choices?.Where(c => c.Label.Contains(keyword)).Select(x => x.Value).ToList();
        return ids?.Any() == true ? (ids, SearchOperation.In) : null;
    }

    protected override bool IsMatchInternal(string dbValue, SearchOperation operation, object filterValue)
    {
        switch (operation)
        {
            case SearchOperation.Equals:
            case SearchOperation.NotEquals:
            {
                var fv = (filterValue as string)!;
                return operation == SearchOperation.Equals ? dbValue == fv : dbValue != fv;
            }
            case SearchOperation.In:
            case SearchOperation.NotIn:
            {
                var fv = (filterValue as List<string>)!;
                return operation == SearchOperation.In ? fv.Contains(dbValue) : !fv.Contains(dbValue);
            }
            default:
                return true;
        }
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?>
        SearchOperations { get; } = new()
    {
        {SearchOperation.Equals, new PropertySearchOperationOptions(PropertyType.SingleChoice)},
        {SearchOperation.NotEquals, new PropertySearchOperationOptions(PropertyType.SingleChoice)},
        {SearchOperation.IsNull, null}, {SearchOperation.IsNotNull, null},
        {
            SearchOperation.In,
            new PropertySearchOperationOptions(PropertyType.MultipleChoice, ConvertToMultipleChoiceForSearchOperation)
        },
        {
            SearchOperation.NotIn,
            new PropertySearchOperationOptions(PropertyType.MultipleChoice, ConvertToMultipleChoiceForSearchOperation)
        },
    };

    private static Bakabase.Abstractions.Models.Domain.Property ConvertToMultipleChoiceForSearchOperation(
        Bakabase.Abstractions.Models.Domain.Property p)
    {
        var options = (p.Options as SingleChoicePropertyOptions);
        return new Bakabase.Abstractions.Models.Domain.Property(p.Pool, p.Id, PropertyType.MultipleChoice, p.Name,
            new MultipleChoicePropertyOptions
            {
                Choices = options?.Choices,
                AllowAddingNewDataDynamically = options?.AllowAddingNewDataDynamically ?? false,
                DefaultValue = string.IsNullOrEmpty(options?.DefaultValue) ? null : [options.DefaultValue]
            });
    }

    protected override (string? DbValue, bool PropertyChanged) PrepareDbValueInternal(
        Bakabase.Abstractions.Models.Domain.Property property, string bizValue)
    {
        bizValue = bizValue.Trim();
        if (!string.IsNullOrEmpty(bizValue))
        {
            property.Options ??= new SingleChoicePropertyOptions {AllowAddingNewDataDynamically = true};
            var options = (property.Options as SingleChoicePropertyOptions)!;
            var propertyChanged = options.AddChoices(true, [bizValue], null);
            var stringValue = options.Choices?.Find(x => x.Label == bizValue)?.Value;
            var nv = new StringValueBuilder(stringValue).Value;
            return (nv, propertyChanged);
        }

        return (null, false);
    }

    protected override string? GetBizValueInternal(Bakabase.Abstractions.Models.Domain.Property property, string value)
    {
        var options = (property.Options as SingleChoicePropertyOptions)!;
        return options?.Choices?.FirstOrDefault(c => c.Value == value)?.Label;
    }
}