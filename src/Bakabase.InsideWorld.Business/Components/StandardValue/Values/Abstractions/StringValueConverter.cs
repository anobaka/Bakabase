using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.CustomProperty.Properties.Text;

namespace Bakabase.InsideWorld.Business.Components.StandardValue.Values.Abstractions
{
    public abstract class StringValueConverter : AbstractStandardValueHandler<string>
    {
        public abstract override StandardValueType Type { get; }
        private readonly SpecialTextService _specialTextService;

        protected StringValueConverter(SpecialTextService specialTextService)
        {
            _specialTextService = specialTextService;
        }

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss =>
            new Dictionary<StandardValueType, StandardValueConversionLoss?>
            {
                {StandardValueType.SingleLineText, null},
                {StandardValueType.MultilineText, null},
                {StandardValueType.Link, null},
                {StandardValueType.SingleChoice, null},
                {StandardValueType.MultipleChoice, null},
                {StandardValueType.Number, StandardValueConversionLoss.InconvertibleDataWillBeLost},
                {StandardValueType.Percentage, StandardValueConversionLoss.InconvertibleDataWillBeLost},
                {StandardValueType.Rating, StandardValueConversionLoss.InconvertibleDataWillBeLost},
                {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                {StandardValueType.Attachment, StandardValueConversionLoss.All},
                {StandardValueType.Date, StandardValueConversionLoss.InconvertibleDataWillBeLost},
                {StandardValueType.DateTime, StandardValueConversionLoss.InconvertibleDataWillBeLost},
                {StandardValueType.Time, StandardValueConversionLoss.InconvertibleDataWillBeLost},
                {StandardValueType.Formula, StandardValueConversionLoss.All},
                {StandardValueType.MultilevelText, StandardValueConversionLoss.All},
            };

        protected override string? ConvertToTypedValue(object? currentValue)
        {
            var str = currentValue as string;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(string currentValue) =>
            (currentValue, null);

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss)
            ConvertToListString(string currentValue) => ([currentValue], null);

        public override (bool? NewValue, StandardValueConversionLoss? Loss) ConvertToBoolean(string currentValue) =>
            (true, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue);

        public override async Task<(DateTime? NewValue, StandardValueConversionLoss? Loss)> ConvertToDateTime(
            string currentValue)
        {
            var date = await _specialTextService.TryToParseDateTime(currentValue);
            return (date, date.HasValue ? null : StandardValueConversionLoss.All);
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToFormula(string currentValue) =>
            (null, StandardValueConversionLoss.All);

        public override (LinkData? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(string currentValue) =>
            (new LinkData { Text = currentValue, Url = currentValue }, null);

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss)
            ConvertToMultilevel(string currentValue) => ([[currentValue]], null);

        public override (decimal? NewValue, StandardValueConversionLoss? Loss) ConvertToNumber(string currentValue)
        {
            return decimal.TryParse(currentValue, out var number)
                ? (number, null)
                : (null, StandardValueConversionLoss.All);
        }

        public override (TimeSpan? NewValue, StandardValueConversionLoss? Loss) ConvertToTime(string currentValue)
        {
            return TimeSpan.TryParse(currentValue, out var time)
                ? (time, null)
                : (null, StandardValueConversionLoss.All);
        }
    }
}