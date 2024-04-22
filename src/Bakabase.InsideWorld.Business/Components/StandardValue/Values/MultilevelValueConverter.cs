using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Properties.Text;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values
{
    public class MultilevelValueConverter : AbstractStandardValueHandler<List<List<string>>>
    {
        public override StandardValueType Type => StandardValueType.Multilevel;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.SingleLineText, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.MultilineText, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.Link, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.SingleChoice, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.MultipleChoice, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.Number, StandardValueConversionLoss.All},
                {StandardValueType.Percentage, StandardValueConversionLoss.All},
                {StandardValueType.Rating, StandardValueConversionLoss.All},
                {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                {StandardValueType.Attachment, StandardValueConversionLoss.All},
                {StandardValueType.Date, StandardValueConversionLoss.All},
                {StandardValueType.DateTime, StandardValueConversionLoss.All},
                {StandardValueType.Time, StandardValueConversionLoss.All},
                {StandardValueType.Formula, StandardValueConversionLoss.All},
                {StandardValueType.Multilevel, null},
            };

        protected override List<List<string>>? ConvertToTypedValue(object? currentValue)
        {
            var data = currentValue as List<List<string>>;
            return data?.Any(x => x?.Any() == true) == true ? data : null;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(
            List<List<string>> currentValue)
        {
            var nv = string.Join(BusinessConstants.LayerTextSeparator,
                currentValue.Select(s => string.Join(BusinessConstants.TextSeparator, s)));
            return (nv, currentValue.Count > 1 || currentValue.Any(s => s.Count > 1)
                ? StandardValueConversionLoss.ValuesWillBeMerged
                : null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            List<List<string>> currentValue)
        {
            var nv = currentValue.Select(s => string.Join(BusinessConstants.TextSeparator, s)).ToList();
            return (nv, currentValue.Any(s => s.Count > 1) ? StandardValueConversionLoss.ValuesWillBeMerged : null);
        }

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(
            List<List<string>> currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(
            List<List<string>> currentValue)
        {
            return (true, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue);
        }

        public override (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(
            List<List<string>> currentValue)
        {
            var (nv, loss) = ConvertToString(currentValue);
            return (new LinkData { Url = nv, Text = nv }, loss);
        }

        public override async Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            List<List<string>> currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(
            List<List<string>> currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(
            List<List<string>> currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            List<List<string>> currentValue)
        {
            return (currentValue, null);
        }
    }
}