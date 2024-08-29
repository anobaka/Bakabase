﻿using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.StandardValue.Abstractions.Components;

/// <summary>
/// This is a *very* abstract layer to define the abilities of a base value in global scope. 
/// </summary>
public interface IStandardValueHandler
{
    StandardValueType Type { get; }
    Dictionary<StandardValueType, StandardValueConversionLoss?> DefaultConversionLoss { get; }
    Task<(object? NewValue, StandardValueConversionLoss? Loss)> Convert(object? currentValue, StandardValueType toType);

    async Task<(T? NewValue, StandardValueConversionLoss? Loss)> Convert<T>(object? currentValue,
        StandardValueType toType)
    {
        var r = await Convert(currentValue, toType);
        return (r.NewValue is T value ? value : default, r.Loss);
    }

    bool ValidateType(object? value);
    Type ExpectedType { get; }
    string? BuildDisplayValue(object? value);
}