﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.StandardValue.Components.ValueHandlers
{
    public class MultilevelValueHandler : AbstractStandardValueHandler<List<List<string>>>
    {
        public override StandardValueType Type => StandardValueType.ListListString;

        public override Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; } =
            new()
            {
                {StandardValueType.String, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.ListString, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.Decimal, StandardValueConversionLoss.All},
                {StandardValueType.Link, StandardValueConversionLoss.ValuesWillBeMerged},
                {StandardValueType.Boolean, StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                {StandardValueType.DateTime, StandardValueConversionLoss.All},
                {StandardValueType.Time, StandardValueConversionLoss.All},
                {StandardValueType.ListListString, null},
                {StandardValueType.ListTag, StandardValueConversionLoss.All}
            };

        protected override string? BuildDisplayValue(List<List<string>> value)
        {
            value = value.RemoveEmpty();
            return string.Join(InternalOptions.TextSeparator,
                value.Select(s => string.Join(InternalOptions.LayerTextSeparator, s)));
        }

        protected override List<List<string>>? ConvertToTypedValue(object? currentValue)
        {
            var data = (currentValue as List<List<string>>)?.RemoveEmpty();
            return data?.Any() == true ? data : null;
        }

        public override (string? NewValue, StandardValueConversionLoss? Loss) ConvertToString(
            List<List<string>> currentValue)
        {
            var nv = string.Join(InternalOptions.LayerTextSeparator,
                currentValue.Select(s => string.Join(InternalOptions.TextSeparator, s)));
            return (nv, currentValue.Count > 1 || currentValue.Any(s => s.Count > 1)
                ? StandardValueConversionLoss.ValuesWillBeMerged
                : null);
        }

        public override (List<string>? NewValue, StandardValueConversionLoss? Loss) ConvertToListString(
            List<List<string>> currentValue)
        {
            var nv = currentValue.Select(s => string.Join(InternalOptions.TextSeparator, s)).ToList();
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

        public override (LinkValue? NewValue, StandardValueConversionLoss? Loss) ConvertToLink(
            List<List<string>> currentValue)
        {
            var (nv, loss) = ConvertToString(currentValue);
            return (new LinkValue { Url = null, Text = nv }, loss);
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

        public override (List<List<string>>? NewValue, StandardValueConversionLoss? Loss) ConvertToMultilevel(
            List<List<string>> currentValue)
        {
            return (currentValue, null);
        }

        public override (List<TagValue>? NewValue, StandardValueConversionLoss? Loss) ConvertToListTag(List<List<string>> currentValue)
        {
            return (null, StandardValueConversionLoss.All);
        }
    }
}