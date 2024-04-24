using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Properties.Text;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values.Abstractions
{
    public abstract class ListStringValueConverter : AbstractStandardValueHandler<List<string>>
    {
        private readonly SpecialTextService _specialTextService;

        protected ListStringValueConverter(SpecialTextService specialTextService)
        {
            _specialTextService = specialTextService;
        }

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.SingleLineText, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.MultilineText, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.Link, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.SingleTextChoice, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.MultipleTextChoice, null},
                {
                    StandardValueType.Number,
                    StandardValueConversionLoss.InconvertibleDataWillBeLost |
                    StandardValueConversionLoss.OnlyFirstNotEmptyValueWillBeRemained
                },
                {
                    StandardValueType.Percentage,
                    StandardValueConversionLoss.InconvertibleDataWillBeLost |
                    StandardValueConversionLoss.OnlyFirstNotEmptyValueWillBeRemained
                },
                {
                    StandardValueType.Rating,
                    StandardValueConversionLoss.InconvertibleDataWillBeLost |
                    StandardValueConversionLoss.OnlyFirstNotEmptyValueWillBeRemained
                },
                {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                {StandardValueType.Attachment, StandardValueConversionLoss.All},
                {
                    StandardValueType.Date,
                    StandardValueConversionLoss.InconvertibleDataWillBeLost |
                    StandardValueConversionLoss.OnlyFirstNotEmptyValueWillBeRemained
                },
                {
                    StandardValueType.DateTime,
                    StandardValueConversionLoss.InconvertibleDataWillBeLost |
                    StandardValueConversionLoss.OnlyFirstNotEmptyValueWillBeRemained
                },
                {
                    StandardValueType.Time,
                    StandardValueConversionLoss.InconvertibleDataWillBeLost |
                    StandardValueConversionLoss.OnlyFirstNotEmptyValueWillBeRemained
                },
                {StandardValueType.Formula, StandardValueConversionLoss.All},
                {StandardValueType.MultipleTextTree, null},
            };

        protected override List<string>? ConvertToTypedValue(object? currentValue)
        {
            if (currentValue is List<string> list)
            {
                list = list.Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList();
                if (list.Any())
                {
                    return list;
                }
            }

            return null;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(
            List<string> currentValue)
        {
            if (currentValue.Count > 1)
            {
                return (string.Join(BusinessConstants.TextSeparator, currentValue),
                    StandardValueConversionLoss.ValuesWillBeMerged);
            }

            return (currentValue[0], null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            List<string> currentValue) => (currentValue, null);

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(
            List<string> currentValue)
        {
            return decimal.TryParse(currentValue[0], out var number)
                ? (number,
                    currentValue.Count > 1 ? StandardValueConversionLoss.OnlyFirstNotEmptyValueWillBeRemained : null)
                : (null, StandardValueConversionLoss.All);
        }

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(List<string> currentValue)
        {
            return (true,
                currentValue.Count > 1 ? StandardValueConversionLoss.OnlyFirstNotEmptyValueWillBeRemained : null);
        }

        public override (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(
            List<string> currentValue)
        {
            var text = string.Join(BusinessConstants.TextSeparator, currentValue);
            var ld = new LinkData { Text = text, Url = text };
            return (ld, currentValue.Count > 1 ? StandardValueConversionLoss.ValuesWillBeMerged : null);
        }

        public override async Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            List<string> currentValue)
        {
            var date = await _specialTextService.TryToParseDateTime(currentValue[0]);
            return (date, date.HasValue ? null : StandardValueConversionLoss.All);
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(
            List<string> currentValue)
        {
            return TimeSpan.TryParse(currentValue[0], out var ts)
                ? (ts, currentValue.Count > 1 ? StandardValueConversionLoss.OnlyFirstNotEmptyValueWillBeRemained : null)
                : (null, StandardValueConversionLoss.All);
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(
            List<string> currentValue) =>
            (null, StandardValueConversionLoss.All);

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss)
            ConvertToMultilevel(List<string> currentValue) =>
            (currentValue.Select(v => new List<string> { v }).ToList(), null);
    }
}