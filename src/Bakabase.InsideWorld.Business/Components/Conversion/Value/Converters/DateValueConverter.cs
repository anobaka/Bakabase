using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Conversion.Value.Converters
{
    public class DateValueConverter : DateTimeValueConverter
    {
        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.SingleLineText, null},
                {StandardValueType.MultilineText, null},
                {StandardValueType.Link, null},
                {StandardValueType.SingleChoice, null},
                {StandardValueType.MultipleChoice, null},
                {StandardValueType.Number, StandardValueConversionLoss.All},
                {StandardValueType.Percentage, StandardValueConversionLoss.All},
                {StandardValueType.Rating, StandardValueConversionLoss.All},
                {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                {StandardValueType.Attachment, StandardValueConversionLoss.All},
                {StandardValueType.Date, null},
                {StandardValueType.DateTime, null},
                {StandardValueType.Time, StandardValueConversionLoss.All},
                {StandardValueType.Formula, StandardValueConversionLoss.All},
                {StandardValueType.Multilevel, StandardValueConversionLoss.All},
            };

        public override StandardValueType Type => StandardValueType.Date;
    }
}