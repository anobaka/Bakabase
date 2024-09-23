using Bakabase.Modules.StandardValue.Abstractions.Configurations;

namespace Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;

[Flags]
public enum StandardValueConversionRule
{
    Directly = 1,
    Incompatible = 1 << 1,
    ValuesWillBeMerged = 1 << 2,

    /// <summary>
    /// 1. False(case-insensitive)/0/empty to false,
    /// 2. Others to true
    /// </summary>
    DateWillBeLost = 1 << 3,

    /// <summary>
    /// 1. Group will be null if there is only one segment after split by :.
    /// 2. Only first part of segments split by : will be treated as group, the following text will be treated as name.
    /// </summary>
    StringToTag = 1 << 4,

    /// <summary>
    /// String will be split by <see cref="StandardValueOptions.ListListStringInnerSeparator"/>
    /// </summary>
    OnlyFirstValidRemains = 1 << 6,
    StringToDateTime = 1 << 7,
    StringToTime = 1 << 8,
    UrlWillBeLost = 1 << 10,
    StringToNumber = 1 << 11,
    Trim = 1 << 13,
    StringToLink = 1 << 14,
    ValueWillBeSplit = 1 << 15,
    BooleanToNumber = 1 << 16,
    TimeToDateTime = 1 << 17,
    TagGroupWillBeLost = 1 << 18,
    ValueToBoolean = 1 << 19
}