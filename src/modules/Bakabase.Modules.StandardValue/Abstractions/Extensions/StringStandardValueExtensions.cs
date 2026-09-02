using System.Globalization;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Components.ValueHandlers;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Abstractions.Extensions;

public static class StringStandardValueExtensions
{
    public static bool? ConvertToBoolean(this string? currentValue)
    {
        currentValue = currentValue?.Trim();

        return currentValue.IsNullOrEmpty()
            ? null
            : currentValue != "0" && !currentValue.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    public static LinkValue? ConvertToLinkValue(this string? value) => LinkValue.TryParse(value);

    public static decimal? ConvertToDecimal(this string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // InvariantCulture first so results don't depend on the machine's locale
        // (matching how decimals are serialized); fall back to CurrentCulture for
        // user-typed regional formats. NumberStyles.Float disallows group
        // separators, removing the "1,234" ambiguity.
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out number) ? number : null;
    }

    public static List<string>? ConvertToListString(this string? value) => value?.Trim()
        .Split(StandardValueSystem.CommonListItemSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim())
        .Where(x => x.IsNotEmpty()).ToList();

    public static List<List<string>>? ConvertToListListString(this string? value) => value?
        .Trim()
        .Split(StandardValueSystem.CommonListItemSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim())
        .Where(x => x.IsNotEmpty())
        .Select(x => x.Split(StandardValueSystem.ListListStringInnerSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(y => y.Trim()).Where(y => y.IsNotEmpty()).ToList())
        .ToList();

    public static List<string>? ConvertToInnerListOfListListString(this string? value)
    {
        var list = value?
            .Trim()
            .Split(StandardValueSystem.ListListStringInnerSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.IsNotEmpty())
            .ToList();
        return list?.Any() == true ? list : null;
    }

    public static List<TagValue>? ConvertToListTag(this string? value) => value?.Trim()
        .Split(StandardValueSystem.CommonListItemSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(TagValue.TryParse)
        .OfType<TagValue>()
        .ToList();

    public static TimeSpan? ConvertToTime(this string? value)
    {
        value = value?.Trim();
        if (value.IsNullOrEmpty())
        {
            return null;
        }

        return TimeSpan.TryParse(value, out var time) ? time : null;
    }

    public static DateTime? ConvertToDateTime(this string? value)
    {
        value = value?.Trim();
        if (value.IsNullOrEmpty())
        {
            return null;
        }

        return DateTime.TryParse(value, out var dt) ? dt : null;
    }
}