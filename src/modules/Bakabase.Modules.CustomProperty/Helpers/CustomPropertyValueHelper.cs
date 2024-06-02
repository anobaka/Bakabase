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
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Helpers
{
    public class CustomPropertyValueHelper
    {
        public static CustomPropertyValue CreateFromImplicitValue(object? typedInnerValue, int type,
            int resourceId, int propertyId,
            int scope)
        {
            CustomPropertyValue pv = (CustomPropertyType) type switch
            {
                CustomPropertyType.SingleLineText => new SingleLineTextPropertyValue
                    {TypedValue = typedInnerValue as string},
                CustomPropertyType.MultilineText => new MultilineTextPropertyValue()
                    {TypedValue = typedInnerValue as string},
                CustomPropertyType.SingleChoice => new SingleChoicePropertyValue
                    {TypedValue = typedInnerValue as string},
                CustomPropertyType.MultipleChoice => new MultipleChoicePropertyValue()
                    {TypedValue = typedInnerValue as List<string>},
                CustomPropertyType.Number => new NumberPropertyValue
                    {TypedValue = typedInnerValue is decimal value1 ? value1 : 0},
                CustomPropertyType.Percentage => new PercentagePropertyValue()
                    {TypedValue = typedInnerValue is decimal value2 ? value2 : 0},
                CustomPropertyType.Rating => new RatingPropertyValue()
                    {TypedValue = typedInnerValue is decimal value3 ? value3 : 0},
                CustomPropertyType.Boolean => new BooleanPropertyValue() {TypedValue = typedInnerValue is true},
                CustomPropertyType.Link => new LinkPropertyValue() {TypedValue = typedInnerValue as LinkData},
                CustomPropertyType.Attachment => new AttachmentPropertyValue()
                    {TypedValue = typedInnerValue as List<string>},
                CustomPropertyType.Date => new DatePropertyValue()
                    {TypedValue = typedInnerValue is DateTime date ? date : default},
                CustomPropertyType.DateTime => new DateTimePropertyValue()
                    {TypedValue = typedInnerValue is DateTime dateTime ? dateTime : default},
                CustomPropertyType.Time => new TimePropertyValue()
                    {TypedValue = typedInnerValue is TimeSpan timeSpan ? timeSpan : default},
                CustomPropertyType.Formula => new FormulaPropertyValue() {TypedValue = typedInnerValue as string},
                CustomPropertyType.Multilevel => new MultilevelPropertyValue()
                    {TypedValue = typedInnerValue as List<string>},
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
            pv.Scope = scope;
            pv.ResourceId = resourceId;
            pv.PropertyId = propertyId;
            return pv;
        }

        public static string? SerializeValue(object? value)
        {
            if (value == null)
            {
                return null;
            }

            return value.ToJson();
        }

        public static object? DeserializeValue<T>(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<T>(value);
        }
    }
}