using System;

namespace Bakabase.InsideWorld.Models.Constants;

[Flags]
public enum StandardValueConversionLoss
{
    /// <summary>
    /// Fully incompatible
    /// </summary>
    All = 1 << 0,
    InconvertibleDataWillBeLost = 1 << 1,
    NotEmptyValueWillBeConvertedToTrue = 1 << 2,
    ValuesWillBeMerged = 1 << 3,
    OnlyFirstNotEmptyValueWillBeRemained = 1 << 4,
    NonZeroValueWillBeConvertedToTrue = 1 << 5,
    TextWillBeLost = 1 << 6,
    TimeWillBeLost = 1 << 7,
    DateWillBeLost = 1 << 8,
    TagGroupAndNameWillBeMerged = 1 << 9,
}