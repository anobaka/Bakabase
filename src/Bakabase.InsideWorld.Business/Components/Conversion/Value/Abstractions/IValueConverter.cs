using System;
using Bakabase.InsideWorld.Models.Constants;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.Conversion.Value.Abstractions;

public interface IValueConverter
{
    StandardValueType Type { get; }
    Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; }
    Task<(object? NewValue, StandardValueConversionLoss? Loss)> Convert(object? currentValue, StandardValueType toType);
    (bool IsValid, Type RequiredType) ValidateType(object? value);
}