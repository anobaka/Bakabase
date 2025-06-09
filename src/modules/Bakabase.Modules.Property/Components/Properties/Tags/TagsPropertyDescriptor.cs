using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Domain;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Tags;

public class TagsPropertyDescriptor : AbstractPropertyDescriptor<TagsPropertyOptions, List<string>, List<TagValue>>
{
    public override PropertyType Type => Bakabase.Abstractions.Models.Domain.Constants.PropertyType.Tags;

    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeywordInternal(
        Bakabase.Abstractions.Models.Domain.Property property,
        string keyword)
    {
        var options = property.Options as TagsPropertyOptions;
        var values = options?.Tags?.Where(t => t.Group?.Contains(keyword) == true || t.Name.Contains(keyword))
            .Select(t => t.Value).ToHashSet();
        return values?.Any() == true ? (values, SearchOperation.In) : null;
    }

    protected override (List<string>? DbValue, bool PropertyChanged) PrepareDbValueInternal(
        Bakabase.Abstractions.Models.Domain.Property property, List<TagValue> bizValue)
    {
        bizValue.TrimAll();
        if (!bizValue.Any())
        {
            return (null, false);
        }

        var dbValue = new List<string>();
        var propertyChanged = false;
        var options =
            ((property.Options ??= new TagsPropertyOptions()) as
                TagsPropertyOptions)!;
        options.Tags ??= [];
        foreach (var tag in bizValue)
        {
            var definedTag = options.Tags.FirstOrDefault(x => x.Name == tag.Name && x.Group == tag.Group);
            if (definedTag == null)
            {
                definedTag = new TagsPropertyOptions.TagOptions(tag.Group, tag.Name)
                    {Value = TagsPropertyOptions.TagOptions.GenerateValue()};
                options.Tags.Add(definedTag);
                propertyChanged = true;
            }

            dbValue.Add(definedTag.Value);
        }

        return (dbValue, propertyChanged);
    }

    protected override List<TagValue>? GetBizValueInternal(Bakabase.Abstractions.Models.Domain.Property property,
        List<string> value)
    {
        var options = property.Options as TagsPropertyOptions;
        return value
            .Select(v => options?.Tags?.FirstOrDefault(x => x.Value == v)?.ToTagValue()).OfType<TagValue>()
            .ToList();
    }

    protected override bool IsMatchInternal(List<string> dbValue, SearchOperation operation, object filterValue)
    {
        var fv = (List<string>) filterValue;
        return operation switch
        {
            SearchOperation.Contains => fv.All(dbValue.Contains),
            SearchOperation.NotContains => !fv.Any(dbValue.Contains),
            SearchOperation.In => dbValue.All(fv.Contains),
            _ => true
        };
    }

    public override Dictionary<SearchOperation, PropertySearchOperationOptions?>
        SearchOperations { get; } = new()
    {
        {SearchOperation.Contains, new PropertySearchOperationOptions(PropertyType.Tags)},
        {SearchOperation.NotContains, new PropertySearchOperationOptions(PropertyType.Tags)},
        {SearchOperation.IsNull, null},
        {SearchOperation.IsNotNull, null},
        {SearchOperation.In, new PropertySearchOperationOptions(PropertyType.Tags)},
    };
}