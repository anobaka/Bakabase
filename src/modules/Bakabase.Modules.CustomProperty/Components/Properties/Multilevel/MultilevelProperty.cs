using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Models;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using NPOI.POIFS.Properties;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Multilevel;

public record MultilevelProperty : CustomProperty<MultilevelPropertyOptions>;

public record MultilevelPropertyValue : CustomPropertyValue<List<string>>;

public class MultilevelPropertyDescriptor(IStandardValueHelper standardValueHelper) : AbstractCustomPropertyDescriptor<MultilevelProperty,
    MultilevelPropertyOptions, MultilevelPropertyValue, List<string>, List<List<string>>>(standardValueHelper)
{
    public override CustomPropertyType EnumType => CustomPropertyType.Multilevel;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Contains,
        SearchOperation.NotContains,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
    ];

    protected override (object DbValue, SearchOperation Operation)? BuildSearchFilterByKeyword(MultilevelProperty property, string keyword)
    {
        var nodes = property.Options?.Data?.Select(d => d.FindNode(x => x.Label.Contains(keyword)))
            .OfType<MultilevelDataOptions>().ToList();
        if (nodes?.Any() != true)
        {
            return null;
        }

        var ids = nodes.ExtractValues(false).ToHashSet();
        return (ids, SearchOperation.In);
    }

    protected override bool IsMatch(List<string>? value, SearchOperation operation, object? filterValue)
    {
        switch (operation)
        {
            case SearchOperation.Contains:
            case SearchOperation.NotContains:
            {
                var typedTarget = filterValue as List<string>;
                if (typedTarget?.Any() != true)
                {
                    return true;
                }

                return operation switch
                {
                    SearchOperation.Contains => typedTarget.All(target => value?.Contains(target) == true),
                    SearchOperation.NotContains => typedTarget.All(target => value?.Contains(target) != true),
                    _ => true,
                };
            }
            case SearchOperation.IsNull:
                return value?.Any() != true;
            case SearchOperation.IsNotNull:
                return value?.Any() == true;
            default:
                return true;
        }
    }

    protected override (List<string>? DbValue, bool PropertyChanged) TypedPrepareDbValueFromBizValue(
        MultilevelProperty property, List<List<string>> bizValue)
    {
        if (bizValue.Any())
        {
            var options = property.Options ??= new MultilevelPropertyOptions();
            var propertyChanged = options.AddBranchOptions(bizValue);

            var branches = options.Data ?? [];
            var values = branches.FindValuesByLabelChains(bizValue).OfType<string>().ToList();

            return (values.Any() ? values : null, propertyChanged);
        }

        return (null, false);
    }
    protected override List<List<string>>? TypedConvertDbValueToBizValue(MultilevelProperty property, List<string> value)
    {
        var data = new List<List<string>>();
        foreach (var v in value)
        {
            if (property.Options?.Data != null)
            {
                foreach (var d in property.Options.Data)
                {
                    var chain = d.FindLabelChain(v);
                    if (chain != null)
                    {
                        data.Add(chain.ToList());
                    }
                }
            }
            else
            {
                data.Add([v]);
            }
        }

        return data;
    }
}