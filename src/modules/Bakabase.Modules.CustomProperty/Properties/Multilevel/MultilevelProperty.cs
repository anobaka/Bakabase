using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Models;

namespace Bakabase.Modules.CustomProperty.Properties.Multilevel;

public record MultilevelProperty : CustomProperty<MultilevelPropertyOptions>;

public record MultilevelPropertyValue : CustomPropertyValue<List<string>>;

public class MultilevelPropertyDescriptor : AbstractCustomPropertyDescriptor<MultilevelProperty,
    MultilevelPropertyOptions, MultilevelPropertyValue, List<string>>
{
    public override CustomPropertyType EnumType => CustomPropertyType.Multilevel;

    public override SearchOperation[] SearchOperations { get; } =
    [
        SearchOperation.Contains,
        SearchOperation.NotContains,
        SearchOperation.IsNull,
        SearchOperation.IsNotNull,
    ];

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

    protected override object? BuildValueForDisplay(MultilevelProperty property, List<string> value)
    {
        var data = new List<List<string>>();
        foreach (var v in value)
        {
            if (property.Options?.Data != null)
            {
                foreach (var d in property.Options.Data)
                {
                    var chain = d.FindLabel(v);
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