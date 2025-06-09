using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Extensions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Property.Components.Properties.Choice;

public record MultipleChoicePropertyOptions : ChoicePropertyOptions<List<string>>;

public class MultipleChoicePropertyDescriptor
    : AbstractPropertyDescriptor<MultipleChoicePropertyOptions, List<string>, List<string>>
{
    public override PropertyType Type => PropertyType.MultipleChoice;

    protected override bool IsMatchInternal(List<string> dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (filterValue as List<string>)!;
        return operation switch
        {
            SearchOperation.Contains => fv.All(dbValue.Contains),
            SearchOperation.NotContains => fv.All(target => !dbValue.Contains(target)),
            SearchOperation.In => dbValue.All(fv.Contains),
            _ => true
        };
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?>
        SearchOperations { get; } = new()
    {
        {SearchOperation.Contains, new PropertySearchOperationOptions(PropertyType.MultipleChoice)},
        {SearchOperation.NotContains, new PropertySearchOperationOptions(PropertyType.MultipleChoice)},
        {SearchOperation.IsNull, null},
        {SearchOperation.IsNotNull, null},
        {SearchOperation.In, new PropertySearchOperationOptions(PropertyType.MultipleChoice)},
    };

    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeywordInternal(
        Bakabase.Abstractions.Models.Domain.Property property, string keyword)
    {
        var options = property.Options as MultipleChoicePropertyOptions;
        var ids = options?.Choices?.Where(c => c.Label.Contains(keyword)).Select(x => x.Value).ToList();
        return ids?.Any() == true ? (ids, SearchOperation.In) : null;
    }

    protected override (List<string>? DbValue, bool PropertyChanged) PrepareDbValueInternal(
        Bakabase.Abstractions.Models.Domain.Property property, List<string> bizValue)
    {
        var goodValues = bizValue.TrimAndRemoveEmpty();
        if (goodValues?.Any() == true)
        {
            property.Options ??= new MultipleChoicePropertyOptions();
            var options = (property.Options as MultipleChoicePropertyOptions)!;
            var propertyChanged = options.AddChoices(true, goodValues.ToArray(), null);
            var stringValues = goodValues.Select(v => options.Choices?.Find(c => c.Label == v)?.Value).OfType<string>()
                .ToList();
            var nv = stringValues.Any() ? new ListStringValueBuilder(stringValues).Value : null;
            return (nv, propertyChanged);
        }

        return (null, false);
    }

    protected override List<string> GetBizValueInternal(Bakabase.Abstractions.Models.Domain.Property property,
        List<string> value)
    {
        var options = property.Options as MultipleChoicePropertyOptions;
        return value.Select(v => options?.Choices?.FirstOrDefault(c => c.Value == v)?.Label ?? v).ToList();
    }
}