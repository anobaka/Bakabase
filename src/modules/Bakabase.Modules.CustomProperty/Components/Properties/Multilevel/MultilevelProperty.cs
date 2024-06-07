using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Models;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using NPOI.POIFS.Properties;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Multilevel;

public record MultilevelProperty : CustomProperty<MultilevelPropertyOptions>;

public record MultilevelPropertyValue : CustomPropertyValue<List<string>>;

public class MultilevelPropertyDescriptor : AbstractCustomPropertyDescriptor<MultilevelProperty,
    MultilevelPropertyOptions, MultilevelPropertyValue, List<string>, List<List<string>>>
{
    public override CustomPropertyType EnumType => CustomPropertyType.Multilevel;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Contains,
        SearchOperation.NotContains,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
    ];

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

    protected override bool IsMatch(List<string>? value, CustomPropertyValueSearchRequestModel model)
    {
        switch (model.Operation)
        {
            case SearchOperation.Contains:
            case SearchOperation.NotContains:
                {
                    var typedTarget = model.DeserializeValue<List<string>>();
                    if (typedTarget?.Any() != true)
                    {
                        return true;
                    }

                    return model.Operation switch
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