using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Services;

namespace Bakabase.Modules.StandardValue.Services;

public class StandardValueService : IStandardValueService
{
    private readonly Dictionary<StandardValueType, IStandardValueHandler> _valueConverters;

    public StandardValueService(IEnumerable<IStandardValueHandler> valueConverters)
    {
        _valueConverters = valueConverters.ToDictionary(d => d.Type, d => d);
    }

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
        var converter = _valueConverters[fromType];
        return await converter.Convert(data, toType);
    }
}