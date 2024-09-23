using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Attachment;
using Bakabase.Modules.CustomProperty.Components.Properties.Boolean;
using Bakabase.Modules.CustomProperty.Components.Properties.Choice;
using Bakabase.Modules.CustomProperty.Components.Properties.DateTime;
using Bakabase.Modules.CustomProperty.Components.Properties.Formula;
using Bakabase.Modules.CustomProperty.Components.Properties.Link;
using Bakabase.Modules.CustomProperty.Components.Properties.Multilevel;
using Bakabase.Modules.CustomProperty.Components.Properties.Number;
using Bakabase.Modules.CustomProperty.Components.Properties.Number.Abstractions;
using Bakabase.Modules.CustomProperty.Components.Properties.Tags;
using Bakabase.Modules.CustomProperty.Components.Properties.Text;
using Bakabase.Modules.CustomProperty.Components.Properties.Time;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomProperty.Helpers
{
    public class CustomPropertyValueHelper
    {
        public static CustomPropertyValue CreateFromImplicitValue(object? dbValue, int customPropertyType,
            int resourceId, int propertyId,
            int scope)
        {
            CustomPropertyValue pv = (CustomPropertyType) customPropertyType switch
            {
                CustomPropertyType.SingleLineText => new SingleLineTextPropertyValue
                    {TypedValue = dbValue as string},
                CustomPropertyType.MultilineText => new MultilineTextPropertyValue()
                    {TypedValue = dbValue as string},
                CustomPropertyType.SingleChoice => new SingleChoicePropertyValue
                    {TypedValue = dbValue as string},
                CustomPropertyType.MultipleChoice => new MultipleChoicePropertyValue()
                    {TypedValue = dbValue as List<string>},
                CustomPropertyType.Number => new NumberPropertyValue
                    {TypedValue = dbValue is decimal value1 ? value1 : 0},
                CustomPropertyType.Percentage => new PercentagePropertyValue()
                    {TypedValue = dbValue is decimal value2 ? value2 : 0},
                CustomPropertyType.Rating => new RatingPropertyValue()
                    {TypedValue = dbValue is decimal value3 ? value3 : 0},
                CustomPropertyType.Boolean => new BooleanPropertyValue() {TypedValue = dbValue is true},
                CustomPropertyType.Link => new LinkPropertyValue() {TypedValue = dbValue as LinkValue},
                CustomPropertyType.Attachment => new AttachmentPropertyValue()
                    {TypedValue = dbValue as List<string>},
                CustomPropertyType.Date => new DatePropertyValue()
                    {TypedValue = dbValue is DateTime date ? date : default},
                CustomPropertyType.DateTime => new DateTimePropertyValue()
                    {TypedValue = dbValue is DateTime dateTime ? dateTime : default},
                CustomPropertyType.Time => new TimePropertyValue()
                    {TypedValue = dbValue is TimeSpan timeSpan ? timeSpan : default},
                CustomPropertyType.Formula => new FormulaPropertyValue() {TypedValue = dbValue as string},
                CustomPropertyType.Multilevel => new MultilevelPropertyValue()
                    {TypedValue = dbValue as List<string>},
                CustomPropertyType.Tags => new TagsPropertyValue {TypedValue = dbValue as List<string>},
                _ => throw new ArgumentOutOfRangeException(nameof(customPropertyType), customPropertyType, null)
            };
            pv.Scope = scope;
            pv.ResourceId = resourceId;
            pv.PropertyId = propertyId;
            return pv;
        }
    }
}