using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions.Models;
using Bakabase.Modules.CustomProperty.Properties.Multilevel;

namespace Bakabase.InsideWorld.Business.Components.StandardValue
{
    public class StandardValueCreator
    {
        public static string CreateTextValue(string value) => value;
        public static decimal CreateNumberValue(decimal number) => number;
        public const decimal StandardMaxValueForRatingValue = 5m;

        public static decimal CreateRatingValue(decimal value, decimal maxValue) =>
            CreateNumberValue(value / maxValue * StandardMaxValueForRatingValue);

        public static List<string> CreateMultipleStringValue(params string[] value) => [..value];
        public static DateTime CreateDateTimeValue(DateTime value) => value;
        public static bool CreateBooleanValue(bool value) => value;
        public static List<MultilevelValue> CreateMultilevelValue(params MultilevelValue[] value) => [..value];
    }
}