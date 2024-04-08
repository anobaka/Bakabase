using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

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
                case StandardValueConversionLoss.MultipleValuesWillBeMerged:
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
                case StandardValueConversionLoss.ChildrenWillBeLost:
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(loss), loss, null);
            }

            return false;
        }
    }
}