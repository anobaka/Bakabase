﻿using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Models.Domain;

namespace Bakabase.Modules.CustomProperty.Properties.Multilevel;

public record MultilevelProperty : CustomProperty<MultilevelPropertyOptions>;

public record MultilevelPropertyValue : CustomPropertyValue<List<string>>;

public class MultilevelPropertyDescriptor : AbstractCustomPropertyDescriptor<MultilevelProperty,
    MultilevelPropertyOptions, MultilevelPropertyValue, List<string>>
{
    public override StandardValueType ValueType => StandardValueType.ListString;
    public override CustomPropertyType Type => CustomPropertyType.Multilevel;

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
}