using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.StandardValue.Abstractions.Services;

public interface IStandardValueService
{
    Task<(object? NewValue, StandardValueConversionLoss? Loss)> CheckConversionLoss(object? data,
        StandardValueType fromType, StandardValueType toType);
}