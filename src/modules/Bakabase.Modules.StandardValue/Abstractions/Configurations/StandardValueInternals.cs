using System.Collections.Concurrent;
using System.Reflection;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bootstrap.Extensions;
using static Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants.StandardValueConversionRule;

namespace Bakabase.Modules.StandardValue.Abstractions.Configurations;

/// <summary>
/// Core implementation for the StandardValue system.
/// Provides handlers, conversion rules, and test data for StandardValue operations.
/// </summary>
internal static class StandardValueInternals
{
    private static readonly ConcurrentBag<IStandardValueHandler> Handlers = new ConcurrentBag<IStandardValueHandler>(
        Assembly.GetExecutingAssembly().GetTypes()
            .Where(s => s is {IsClass: true, IsAbstract: false, IsPublic: true} &&
                        s.IsAssignableTo(SpecificTypeUtils<IStandardValueHandler>.Type))
            .Select(x => (Activator.CreateInstance(x) as IStandardValueHandler)!));

    /// <summary>
    /// Value handlers by type.
    /// </summary>
    public static readonly ConcurrentDictionary<StandardValueType, IStandardValueHandler> HandlerMap =
        new(Handlers.ToDictionary(d => d.Type, d => d));

    /// <summary>
    /// Expected conversion test data.
    /// </summary>
    public static readonly Dictionary<StandardValueType,
            Dictionary<StandardValueType, List<(object? FromValue, object? ExpectedValue)>>> ExpectedConversions =
            SpecificEnumUtils<StandardValueType>.Values.ToDictionary(d => d, fromType =>
                SpecificEnumUtils<StandardValueType>.Values.ToDictionary(c => c,
                    toType =>
                    {
                        var fieldKey = $"{fromType}To{toType}";
                        var field = SpecificTypeUtils<ExpectedConversions>.Type.GetField(fieldKey)!;
                        var list = field.GetValue(null)!;
                        var listType = list.GetType();
                        var count = (int)listType.GetProperty("Count")!.GetValue(list)!;
                        var propInfo = listType.GetProperty("Item")!;

                        var dataSet = new List<(object? FromValue, object? ExpectedValue)>();
                        for (var i = 0; i < count; i++)
                        {
                            var tuple = propInfo.GetValue(list, [i])!;
                            var tupleType = tuple.GetType();
                            var fromValueField = tupleType.GetField($"Item1")!;
                            var expectedValueField = tupleType.GetField($"Item2")!;
                            dataSet.Add((fromValueField.GetValue(tuple), expectedValueField.GetValue(tuple)));
                        }

                        // Console.WriteLine($@"{fromType.ToString(),-20}{toType.ToString(),-20}{dataSet.Count}");

                        return dataSet;
                    }));

    /// <summary>
    /// Conversion rules between StandardValueTypes.
    /// </summary>
    public static readonly Dictionary<StandardValueType, Dictionary<StandardValueType, StandardValueConversionRule>>
        ConversionRules = new()
        {
            {
                StandardValueType.String, new()
                {
                    {StandardValueType.String, Trim},
                    {StandardValueType.ListString, Trim | ValueWillBeSplit},
                    {StandardValueType.Decimal, StringToNumber},
                    {StandardValueType.Link, Trim | StringToLink},
                    {StandardValueType.Boolean, ValueToBoolean | Trim},
                    {StandardValueType.DateTime, Trim | StringToDateTime},
                    {StandardValueType.Time, Trim | StringToTime},
                    {StandardValueType.ListListString, Trim | ValueWillBeSplit},
                    {StandardValueType.ListTag, Trim | ValueWillBeSplit}
                }
            },
            {
                StandardValueType.ListString, new()
                {
                    {StandardValueType.String, ValuesWillBeMerged | Trim},
                    {StandardValueType.ListString, Trim},
                    {StandardValueType.Decimal, Trim | StringToNumber | OnlyFirstValidRemains},
                    {StandardValueType.Link, Trim | ValuesWillBeMerged | StringToLink},
                    {StandardValueType.Boolean, Trim | ValueToBoolean},
                    {StandardValueType.DateTime, Trim | StringToDateTime | OnlyFirstValidRemains},
                    {StandardValueType.Time, Trim | OnlyFirstValidRemains | StringToTime},
                    {StandardValueType.ListListString, Trim | ValuesWillBeMerged},
                    {StandardValueType.ListTag, Trim | StringToTag}
                }
            },
            {
                StandardValueType.Decimal, new()
                {
                    {StandardValueType.String, Directly},
                    {StandardValueType.ListString, Directly},
                    {StandardValueType.Decimal, Directly},
                    {StandardValueType.Link, Directly},
                    {StandardValueType.Boolean, ValueToBoolean},
                    {StandardValueType.DateTime, Incompatible},
                    {StandardValueType.Time, Incompatible},
                    {StandardValueType.ListListString, Directly},
                    {StandardValueType.ListTag, Directly}
                }
            },
            {
                StandardValueType.Link, new()
                {
                    {StandardValueType.String, Trim | Directly},
                    {StandardValueType.ListString, Trim | Directly},
                    {StandardValueType.Decimal, Trim | UrlWillBeLost | StringToNumber},
                    {StandardValueType.Link, Trim},
                    {StandardValueType.Boolean, Trim | UrlWillBeLost | ValueToBoolean},
                    {StandardValueType.DateTime, Trim | StringToDateTime},
                    {StandardValueType.Time, Trim | StringToTime},
                    {StandardValueType.ListListString, Directly},
                    {StandardValueType.ListTag, Directly}
                }
            },
            {
                StandardValueType.Boolean, new()
                {
                    {StandardValueType.String, BooleanToNumber},
                    {StandardValueType.ListString, BooleanToNumber},
                    {StandardValueType.Decimal, BooleanToNumber},
                    {StandardValueType.Link, BooleanToNumber},
                    {StandardValueType.Boolean, Directly},
                    {StandardValueType.DateTime, Incompatible},
                    {StandardValueType.Time, Incompatible},
                    {StandardValueType.ListListString, BooleanToNumber},
                    {StandardValueType.ListTag, BooleanToNumber}
                }
            },
            {
                StandardValueType.DateTime, new()
                {
                    {StandardValueType.String, Directly},
                    {StandardValueType.ListString, Directly},
                    {StandardValueType.Decimal, Incompatible},
                    {StandardValueType.Link, Directly},
                    {StandardValueType.Boolean, ValueToBoolean},
                    {StandardValueType.DateTime, Directly},
                    {StandardValueType.Time, DateWillBeLost},
                    {StandardValueType.ListListString, Directly},
                    {StandardValueType.ListTag, Directly}
                }
            },
            {
                StandardValueType.Time, new()
                {
                    {StandardValueType.String, Directly},
                    {StandardValueType.ListString, Directly},
                    {StandardValueType.Decimal, Incompatible},
                    {StandardValueType.Link, Directly},
                    {StandardValueType.Boolean, ValueToBoolean},
                    {StandardValueType.DateTime, TimeToDateTime},
                    {StandardValueType.Time, Directly},
                    {StandardValueType.ListListString, Directly},
                    {StandardValueType.ListTag, Directly}
                }
            },
            {
                StandardValueType.ListListString, new()
                {
                    {StandardValueType.String, Trim | ValuesWillBeMerged},
                    {StandardValueType.ListString, Trim | ValuesWillBeMerged},
                    {StandardValueType.Decimal, Trim | StringToNumber | OnlyFirstValidRemains},
                    {StandardValueType.Link, Trim | ValuesWillBeMerged | StringToLink},
                    {StandardValueType.Boolean, Trim | ValueToBoolean},
                    {StandardValueType.DateTime, Trim | StringToDateTime | OnlyFirstValidRemains},
                    {StandardValueType.Time, Trim | OnlyFirstValidRemains | StringToTime},
                    {StandardValueType.ListListString, Trim},
                    {StandardValueType.ListTag, Trim | StringToTag | ValuesWillBeMerged}
                }
            },
            {
                StandardValueType.ListTag, new()
                {
                    {StandardValueType.String, Trim | ValuesWillBeMerged},
                    {StandardValueType.ListString, Trim | ValuesWillBeMerged},
                    {StandardValueType.Decimal, Trim | StringToNumber | OnlyFirstValidRemains | TagGroupWillBeLost},
                    {StandardValueType.Link, Trim | ValuesWillBeMerged | StringToLink},
                    {StandardValueType.Boolean, Trim | ValueToBoolean | TagGroupWillBeLost},
                    {StandardValueType.DateTime, Trim | StringToDateTime | OnlyFirstValidRemains | TagGroupWillBeLost},
                    {StandardValueType.Time, Trim | OnlyFirstValidRemains | StringToTime | TagGroupWillBeLost},
                    {StandardValueType.ListListString, Trim | ValueWillBeSplit},
                    {StandardValueType.ListTag, Trim}
                }
            },
        };
}