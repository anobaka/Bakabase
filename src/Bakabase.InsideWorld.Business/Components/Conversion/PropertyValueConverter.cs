using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.StandardValue;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Helpers;
using Bakabase.Modules.CustomProperty.Properties.Choice;
using Bakabase.Modules.CustomProperty.Properties.Choice.Abstractions;
using Bakabase.Modules.CustomProperty.Properties.Multilevel;

namespace Bakabase.InsideWorld.Business.Components.Conversion;

public class PropertyValueConverter
{
    private readonly Dictionary<StandardValueType, IStandardValueHandler> _converters;

    public PropertyValueConverter(IEnumerable<IStandardValueHandler> converters)
    {
        _converters = converters.ToDictionary(d => d.Type, d => d);
    }

    private TProperty CastProperty<TProperty>(CustomProperty toProperty)
    {
        if (toProperty is not TProperty typedProperty)
        {
            throw new DevException($"Can not cast {nameof(toProperty)} to {toProperty.Type}");
        }

        return typedProperty;
    }

    public async Task<CustomPropertyValue> Convert(StandardValueType currentValueType,
        object? currentValue, CustomProperty toProperty)
    {
        var (nv, _) = await _converters[currentValueType].Convert(currentValue, toProperty.ValueType);
        switch ((CustomPropertyType)toProperty.Type)
        {
            case CustomPropertyType.SingleLineText:
            case CustomPropertyType.MultilineText:
                break;
            case CustomPropertyType.SingleChoice:
            {
                var typedProperty = CastProperty<SingleChoiceProperty>(toProperty);
                var typedValue = currentValue as string;
                if (!string.IsNullOrEmpty(typedValue))
                {
                    (typedProperty.Options ??= new ChoicePropertyOptions<string>()).AddChoices(true, typedValue);
                    nv = new StringValueBuilder(typedProperty.Options.Choices!.First(x => x.Label == typedValue).Value);
                }

                break;
            }
            case CustomPropertyType.MultipleChoice:
            {
                var typedProperty = CastProperty<MultipleChoiceProperty>(toProperty);
                var typedValue = currentValue as string[];

                if (typedValue?.Any() == true)
                {
                    (typedProperty.Options ??= new ChoicePropertyOptions<List<string>>()).AddChoices(true, typedValue);
                    nv = typedValue
                        .Select(v => typedProperty.Options.Choices!.First(x => x.Label == v).Value).ToArray();
                }

                break;
            }
            case CustomPropertyType.Number:
            case CustomPropertyType.Percentage:
            case CustomPropertyType.Rating:
            case CustomPropertyType.Boolean:
            case CustomPropertyType.Link:
            case CustomPropertyType.Attachment:
            case CustomPropertyType.Date:
            case CustomPropertyType.DateTime:
            case CustomPropertyType.Time:
            case CustomPropertyType.Formula:
                break;
            case CustomPropertyType.Multilevel:
            {
                var typedProperty = CastProperty<MultilevelProperty>(toProperty);
                var typedValue = nv as List<string>;
                if (typedValue?.Any() == true)
                {
                    var options = typedProperty.Options ??= new MultilevelPropertyOptions();
                    options.Data ??= [];
                    MultilevelDataOptions? parent = null;
                    foreach (var tv in typedValue)
                    {
                        var children = parent == null ? options.Data : (parent.Children ??= []);
                        var child = children.FirstOrDefault(x => x.Label == tv) ??
                                    new MultilevelDataOptions {Label = tv};
                        children.Add(child);
                        parent = child;
                    }
                }

                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }


        return CustomPropertyValueHelper.CreateFromImplicitValue(nv, (CustomPropertyType)toProperty.Type);
    }
}