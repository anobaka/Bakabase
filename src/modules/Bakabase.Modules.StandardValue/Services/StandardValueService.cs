using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Services;

namespace Bakabase.Modules.StandardValue.Services;

public class StandardValueService(ICustomDateTimeParser datetimeParser) : IStandardValueService
{
    /// <summary>
    ///
    /// </summary>
    /// <param name="data"></param>
    /// <param name="fromType"></param>
    /// <param name="toType"></param>
    /// <returns>Loss information</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public async Task<object?> Convert(object? data, StandardValueType fromType, StandardValueType toType)
    {
        var converter = StandardValueSystem.GetHandler(fromType);
        var output = converter.Convert(data, toType);
        if (output == null && (toType == StandardValueType.DateTime) && data != null)
        {
            var textsForDateTime = converter.ExtractTextsForConvertingToDateTime(data);
            if (textsForDateTime?.Any() == true)
            {
                foreach (var t in textsForDateTime)
                {
                    var date = await datetimeParser.TryToParseDateTime(t);
                    if (date.HasValue)
                    {
                        output = date.Value;
                        break;
                    }
                }
            }
        }

        return output;
    }
}