using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class DateValueConverter : DateTimeValueConverter
    {
        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.SingleLineText, null},
                {StandardValueType.MultilineText, null},
                {StandardValueType.Link, null},
                {StandardValueType.SingleTextChoice, null},
                {StandardValueType.MultipleTextChoice, null},
                {StandardValueType.Number, StandardValueConversionLoss.All},
                {StandardValueType.Percentage, StandardValueConversionLoss.All},
                {StandardValueType.Rating, StandardValueConversionLoss.All},
                {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                {StandardValueType.Attachment, StandardValueConversionLoss.All},
                {StandardValueType.Date, null},
                {StandardValueType.DateTime, null},
                {StandardValueType.Time, StandardValueConversionLoss.All},
                {StandardValueType.Formula, StandardValueConversionLoss.All},
                {StandardValueType.MultipleTextTree, StandardValueConversionLoss.All},
            };

        public override StandardValueType Type => StandardValueType.Date;
    }
}