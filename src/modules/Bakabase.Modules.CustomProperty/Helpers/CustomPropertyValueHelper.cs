using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Properties.Attachment;
using Bakabase.Modules.CustomProperty.Properties.Boolean;
using Bakabase.Modules.CustomProperty.Properties.Choice;
using Bakabase.Modules.CustomProperty.Properties.DateTime;
using Bakabase.Modules.CustomProperty.Properties.Formula;
using Bakabase.Modules.CustomProperty.Properties.Multilevel;
using Bakabase.Modules.CustomProperty.Properties.Number;
using Bakabase.Modules.CustomProperty.Properties.Number.Abstractions;
using Bakabase.Modules.CustomProperty.Properties.Text;
using Bakabase.Modules.CustomProperty.Properties.Time;

namespace Bakabase.Modules.CustomProperty.Helpers
{
    public class CustomPropertyValueHelper
    {
        public static CustomPropertyValue CreateFromImplicitValue(object? typedInnerValue, CustomPropertyType type)
        {
            switch (type)
            {
                case CustomPropertyType.SingleLineText:
                    return new SingleLineTextPropertyValue {Value = typedInnerValue as string};
                case CustomPropertyType.MultilineText:
                    return new MultilineTextPropertyValue() {Value = typedInnerValue as string };
                case CustomPropertyType.SingleChoice:
                    return new SingleChoicePropertyValue {Value = typedInnerValue as string};
                case CustomPropertyType.MultipleChoice:
                    return new MultipleChoicePropertyValue() {Value = typedInnerValue as List<string> };
                case CustomPropertyType.Number:
                    return new NumberPropertyValue {Value = typedInnerValue is decimal value1 ? value1 : 0};
                case CustomPropertyType.Percentage:
                    return new PercentagePropertyValue() {Value = typedInnerValue is decimal value2 ? value2 : 0};
                case CustomPropertyType.Rating:
                    return new RatingPropertyValue() {Value = typedInnerValue is decimal value3 ? value3 : 0};
                case CustomPropertyType.Boolean:
                    return new BooleanPropertyValue() {Value = typedInnerValue is true};
                case CustomPropertyType.Link:
                    return new LinkPropertyValue() {Value = typedInnerValue as LinkData};
                case CustomPropertyType.Attachment:
                    return new AttachmentPropertyValue() {Value = typedInnerValue as List<string> };
                case CustomPropertyType.Date:
                    return new DatePropertyValue() {Value = typedInnerValue is DateTime date ? date : default};
                case CustomPropertyType.DateTime:
                    return new DateTimePropertyValue()
                        {Value = typedInnerValue is DateTime dateTime ? dateTime : default};
                case CustomPropertyType.Time:
                    return new TimePropertyValue() {Value = typedInnerValue is TimeSpan timeSpan ? timeSpan : default};
                case CustomPropertyType.Formula:
                    return new FormulaPropertyValue() {Value = typedInnerValue as string};
                case CustomPropertyType.Multilevel:
                    return new MultilevelPropertyValue() {Value = typedInnerValue as List<string> };
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}