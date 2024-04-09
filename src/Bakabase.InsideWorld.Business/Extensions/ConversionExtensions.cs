using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class ConversionExtensions
    {
        public static bool DependsOnValue(this StandardValueConversionLoss loss)
        {
            switch (loss)
            {
                case StandardValueConversionLoss.None:
                    break;
                case StandardValueConversionLoss.All:
                    break;
                case StandardValueConversionLoss.InconvertibleDataWillBeLost:
                    return true;
                case StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue:
                    return true;
                case StandardValueConversionLoss.ValuesWillBeMerged:
                    return true;
                case StandardValueConversionLoss.OnlyFirstValueWillBeRemained:
                    return true;
                case StandardValueConversionLoss.NonZeroValueWillBeConvertedToTrue:
                    return true;
                case StandardValueConversionLoss.TextWillBeLost:
                    break;
                case StandardValueConversionLoss.TimeWillBeLost:
                    break;
                case StandardValueConversionLoss.DateWillBeLost:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(loss), loss, null);
            }

            return false;
        }

        public static StandardValueConversionLoss GetEstimatedLoss(this StandardValueType from, StandardValueType to)
        {
            return BusinessConstants.StandardValueConversionLoss[from][to];
        }

        public static StandardValueConversionLoss[] GetFlags(this StandardValueConversionLoss loss)
        {
            return SpecificEnumUtils<StandardValueConversionLoss>.Values.Where(f => loss.HasFlag(f)).ToArray();
        }

        public static IDictionary<StandardValueType, StandardValueConversionLoss> GetAllConversionCandidates(
            this StandardValueType from)
        {
            return BusinessConstants.StandardValueConversionLoss[from];
        }
    }
}