using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Extensions;
using Bootstrap.Extensions;

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
            if (fromType == toType)
            {
                return StandardValueConversionLoss.None;
            }

            var fallbackLoss = BusinessConstants.StandardValueConversionLoss[fromType][toType];
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

                        break;
                    }
                    case StandardValueType.SingleChoice:
                    {
                        var typedData = data as string;
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
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.MultipleChoice:
                    {
                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                                break;
                            case StandardValueType.MultilineText:
                                break;
                            case StandardValueType.SingleChoice:
                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                                break;
                            case StandardValueType.Percentage:
                                break;
                            case StandardValueType.Rating:
                                break;
                            case StandardValueType.Boolean:
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.Number:
                    {
                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                                break;
                            case StandardValueType.MultilineText:
                                break;
                            case StandardValueType.SingleChoice:
                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                                break;
                            case StandardValueType.Percentage:
                                break;
                            case StandardValueType.Rating:
                                break;
                            case StandardValueType.Boolean:
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.Percentage:
                    {
                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                                break;
                            case StandardValueType.MultilineText:
                                break;
                            case StandardValueType.SingleChoice:
                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                                break;
                            case StandardValueType.Percentage:
                                break;
                            case StandardValueType.Rating:
                                break;
                            case StandardValueType.Boolean:
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.Rating:
                    {
                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                                break;
                            case StandardValueType.MultilineText:
                                break;
                            case StandardValueType.SingleChoice:
                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                                break;
                            case StandardValueType.Percentage:
                                break;
                            case StandardValueType.Rating:
                                break;
                            case StandardValueType.Boolean:
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.Boolean:
                    {
                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                                break;
                            case StandardValueType.MultilineText:
                                break;
                            case StandardValueType.SingleChoice:
                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                                break;
                            case StandardValueType.Percentage:
                                break;
                            case StandardValueType.Rating:
                                break;
                            case StandardValueType.Boolean:
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.Link:
                    {
                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                                break;
                            case StandardValueType.MultilineText:
                                break;
                            case StandardValueType.SingleChoice:
                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                                break;
                            case StandardValueType.Percentage:
                                break;
                            case StandardValueType.Rating:
                                break;
                            case StandardValueType.Boolean:
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.Attachment:
                    {
                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                                break;
                            case StandardValueType.MultilineText:
                                break;
                            case StandardValueType.SingleChoice:
                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                                break;
                            case StandardValueType.Percentage:
                                break;
                            case StandardValueType.Rating:
                                break;
                            case StandardValueType.Boolean:
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.Date:
                    {
                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                                break;
                            case StandardValueType.MultilineText:
                                break;
                            case StandardValueType.SingleChoice:
                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                                break;
                            case StandardValueType.Percentage:
                                break;
                            case StandardValueType.Rating:
                                break;
                            case StandardValueType.Boolean:
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.DateTime:
                    {
                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                                break;
                            case StandardValueType.MultilineText:
                                break;
                            case StandardValueType.SingleChoice:
                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                                break;
                            case StandardValueType.Percentage:
                                break;
                            case StandardValueType.Rating:
                                break;
                            case StandardValueType.Boolean:
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.Time:
                    {
                        switch (toType)
                        {
                            case StandardValueType.SingleLineText:
                                break;
                            case StandardValueType.MultilineText:
                                break;
                            case StandardValueType.SingleChoice:
                                break;
                            case StandardValueType.MultipleChoice:
                                break;
                            case StandardValueType.Number:
                                break;
                            case StandardValueType.Percentage:
                                break;
                            case StandardValueType.Rating:
                                break;
                            case StandardValueType.Boolean:
                                break;
                            case StandardValueType.Link:
                                break;
                            case StandardValueType.Attachment:
                                break;
                            case StandardValueType.Date:
                                break;
                            case StandardValueType.DateTime:
                                break;
                            case StandardValueType.Time:
                                break;
                            case StandardValueType.Formula:
                                break;
                            case StandardValueType.Multilevel:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(toType), toType, null);
                        }

                        break;
                    }
                    case StandardValueType.Formula:
                    case StandardValueType.Multilevel:
                    default:
                        throw new ArgumentOutOfRangeException(nameof(fromType), fromType, null);
                }
            }

            return fallbackLoss;

        }
    }
}