using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Extensions;
using Bootstrap.Extensions;
using Bakabase.Modules.CustomProperty.Properties.Text;

namespace Bakabase.InsideWorld.Business.Services
{
    public class ConversionService
    {
        private readonly SpecialTextService _specialTextService;

        public ConversionService(SpecialTextService specialTextService)
        {
            _specialTextService = specialTextService;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="fromType"></param>
        /// <param name="toType"></param>
        /// <returns>Loss information</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public async Task<StandardValueConversionLoss> CheckConversionLoss<T>(T data, StandardValueType fromType,
            StandardValueType toType)
        {
            if (fromType == toType || data == null)
            {
                return StandardValueConversionLoss.None;
            }

            var fallbackLoss = fromType.GetEstimatedLoss(toType);
            var fallbackLossFlags = SpecificEnumUtils<StandardValueConversionLoss>.Values
                .Where(x => fallbackLoss.HasFlag(x)).ToList();

            if (fallbackLossFlags.All(x => x.DependsOnValue()))
            {
                switch (fromType)
                {
                    case StandardValueType.SingleLineText:
                    case StandardValueType.MultilineText:
                    {
                        var typedData = data as string;
                        if (!string.IsNullOrEmpty(typedData))
                        {
                            switch (toType)
                            {
                                case StandardValueType.Number:
                                case StandardValueType.Percentage:
                                case StandardValueType.Rating:
                                    if (!decimal.TryParse(typedData, out var n))
                                    {
                                        return StandardValueConversionLoss.All;
                                    }

                                    break;
                                case StandardValueType.Boolean:
                                    return StandardValueConversionLoss.All;
                                case StandardValueType.Date:
                                case StandardValueType.DateTime:
                                    if (!(await _specialTextService.TryToParseDateTime(typedData)).HasValue)
                                    {
                                        return StandardValueConversionLoss.All;
                                    }

                                    break;
                                case StandardValueType.Time:
                                    if (!TimeSpan.TryParse(typedData, out var _))
                                    {
                                        return StandardValueConversionLoss.All;
                                    }

                                    break;
                            }
                        }
                        else
                        {
                            return StandardValueConversionLoss.None;
                        }

                        break;
                    }
                    case StandardValueType.SingleChoice:
                    {
                        var typedData = data as string;
                        if (string.IsNullOrEmpty(typedData))
                        {
                            return StandardValueConversionLoss.None;
                        }

                        switch (toType)
                        {
                            case StandardValueType.Number:
                            case StandardValueType.Percentage:
                            case StandardValueType.Rating:
                                if (!decimal.TryParse(typedData, out var n))
                                {
                                    return StandardValueConversionLoss.All;
                                }

                                break;
                            case StandardValueType.Boolean:
                                return StandardValueConversionLoss.All;
                            case StandardValueType.Date:
                            case StandardValueType.DateTime:
                                if (!(await _specialTextService.TryToParseDateTime(typedData)).HasValue)
                                {
                                    return StandardValueConversionLoss.All;
                                }

                                break;
                            case StandardValueType.Time:
                                if (!TimeSpan.TryParse(typedData, out var _))
                                {
                                    return StandardValueConversionLoss.All;
                                }

                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.MultipleChoice:
                    {
                        var typedData = data as List<string>;
                        if (typedData?.Any() != true)
                        {
                            return StandardValueConversionLoss.None;
                        }

                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                            case StandardValueType.MultilineText:
                            case StandardValueType.SingleChoice:
                            case StandardValueType.Link:
                                if (typedData.Count > 1)
                                {
                                    return StandardValueConversionLoss.ValuesWillBeMerged;
                                }

                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                            case StandardValueType.Percentage:
                            case StandardValueType.Rating:
                            {
                                var loss = StandardValueConversionLoss.None;
                                if (typedData.Count > 1)
                                {
                                    loss |= StandardValueConversionLoss.OnlyFirstValueWillBeRemained;
                                }

                                if (!decimal.TryParse(typedData[0], out var d))
                                {
                                    loss |= StandardValueConversionLoss.All;
                                }

                                return loss;
                            }
                            case StandardValueType.Boolean:
                                return StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue;
                            case StandardValueType.Date:
                            case StandardValueType.DateTime:
                            {
                                var loss = StandardValueConversionLoss.None;
                                if (typedData.Count > 1)
                                {
                                    loss |= StandardValueConversionLoss.OnlyFirstValueWillBeRemained;
                                }

                                if (!(await _specialTextService.TryToParseDateTime(typedData[0])).HasValue)
                                {
                                    loss |= StandardValueConversionLoss.All;
                                }

                                return loss;
                            }
                            case StandardValueType.Time:
                            {
                                var loss = StandardValueConversionLoss.None;
                                if (typedData.Count > 1)
                                {
                                    loss |= StandardValueConversionLoss.OnlyFirstValueWillBeRemained;
                                }

                                if (!TimeSpan.TryParse(typedData[0], out var _))
                                {
                                    return StandardValueConversionLoss.All;
                                }

                                return loss;
                            }
                        }

                        break;
                    }
                    case StandardValueType.Number:
                    case StandardValueType.Percentage:
                    case StandardValueType.Rating:
                    {
                        if (data is decimal typedData)
                        {
                            switch (toType)
                            {
                                case StandardValueType.Boolean:
                                    return StandardValueConversionLoss.NonZeroValueWillBeConvertedToTrue;
                            }

                            break;
                        }

                        return StandardValueConversionLoss.None;
                    }
                    case StandardValueType.Link:
                    {
                        if (data is LinkData typedData)
                        {
                            switch (toType)
                            {
                                case StandardValueType.SingleLineText:
                                case StandardValueType.MultilineText:
                                case StandardValueType.SingleChoice:
                                case StandardValueType.MultipleChoice:
                                case StandardValueType.Attachment:
                                    return StandardValueConversionLoss.TextWillBeLost;
                            }
                        }

                        return StandardValueConversionLoss.None;
                    }
                    case StandardValueType.Date:
                    {
                        if (!(data is DateTime typedData))
                        {
                            return StandardValueConversionLoss.None;
                        }

                        switch (toType)
                        {
                            case StandardValueType.Boolean:
                                return StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue;
                        }

                        break;
                    }
                    case StandardValueType.DateTime:
                    {
                        if (data is DateTime)
                        {
                            switch (toType)
                            {
                                case StandardValueType.Boolean:
                                    return StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue;
                                case StandardValueType.Date:
                                    return StandardValueConversionLoss.TimeWillBeLost;
                                case StandardValueType.Time:
                                    return StandardValueConversionLoss.DateWillBeLost;
                            }
                        }
                        else
                        {
                            return StandardValueConversionLoss.None;
                        }

                        break;
                    }
                    case StandardValueType.Time:
                    {
                        if (data is TimeSpan)
                        {
                            switch (toType)
                            {
                                case StandardValueType.Boolean:
                                    return StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue;
                            }
                        }
                        else
                        {
                            return StandardValueConversionLoss.None;
                        }

                        break;
                    }
                    case StandardValueType.Multilevel:
                    {
                        if (data is List<string> link && link.Any())
                        {
                            switch (toType)
                            {
                                case StandardValueType.SingleLineText:
                                case StandardValueType.MultilineText:
                                case StandardValueType.SingleChoice:
                                case StandardValueType.Link:
                                case StandardValueType.MultipleChoice:
                                {
                                    return link.Count > 1
                                        ? StandardValueConversionLoss.ValuesWillBeMerged
                                        : StandardValueConversionLoss.None;
                                }
                            }
                        }
                        else
                        {
                            return StandardValueConversionLoss.None;
                        }

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(fromType), fromType, null);
                }
            }

            return fallbackLoss;
        }
    }
}