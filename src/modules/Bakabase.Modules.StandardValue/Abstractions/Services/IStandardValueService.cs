using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.StandardValue.Abstractions.Services;

public interface IStandardValueService
{
    Task<object?> Convert(object? data, StandardValueType fromType, StandardValueType toType);
}