﻿using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Microsoft.Extensions.Localization;

namespace Bakabase.Modules.Enhancer.Components;

internal class EnhancerLocalizer(IStringLocalizer<EnhancerResource> localizer) : IEnhancerLocalizer
{
    public string Enhancer_Name(EnhancerId enhancerId)
    {
        return localizer[$"Enhancer_{enhancerId}_Name"];
    }

    public string? Enhancer_Description(EnhancerId enhancerId)
    {
        var d = localizer[$"Enhancer_{enhancerId}_Description"];
        if (d.ResourceNotFound)
        {
            return null;
        }

        return d;
    }

    public string Enhancer_TargetName(EnhancerId enhancerId, Enum target)
    {
        return localizer[$"Enhancer_{enhancerId}_Target_{target}_Name"];
    }

    public string? Enhancer_TargetDescription(EnhancerId enhancerId, Enum target)
    {
        var d = localizer[$"Enhancer_{enhancerId}_Target_{target}_Description"];
        if (d.ResourceNotFound)
        {
            return null;
        }

        return d;
    }

    public string Enhancer_Target_Options_PropertyTypeIsNotSupported(PropertyPool type)
    {
        return localizer[nameof(Enhancer_Target_Options_PropertyTypeIsNotSupported), $"{(int) type}:{type}"];
    }

    public string Enhancer_Target_Options_PropertyIdIsNullButPropertyTypeIsNot(PropertyPool type, string target)
    {
        return localizer[nameof(Enhancer_Target_Options_PropertyIdIsNullButPropertyTypeIsNot), $"{(int) type}:{type}", target];
    }

    public string Enhancer_Target_Options_PropertyTypeIsNullButPropertyIdIsNot(int id, string target)
    {
        return localizer[nameof(Enhancer_Target_Options_PropertyTypeIsNullButPropertyIdIsNot), id, target];
    }

    public string Enhancer_Target_Options_PropertyIdIsNotFoundInReservedResourceProperties(int id)
    {
        return localizer[nameof(Enhancer_Target_Options_PropertyIdIsNotFoundInReservedResourceProperties), id];
    }

    public string Enhancer_Target_Options_PropertyIdIsNotFoundInCustomResourceProperties(int id)
    {
        return localizer[nameof(Enhancer_Target_Options_PropertyIdIsNotFoundInCustomResourceProperties), id];
    }
}