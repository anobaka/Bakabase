using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Microsoft.Extensions.Localization;

namespace Bakabase.Modules.Enhancer.Components;

internal class EnhancerLocalizer : IEnhancerLocalizer
{
    private IStringLocalizer<EnhancerResource> _localizer;

    public EnhancerLocalizer(IStringLocalizer<EnhancerResource> localizer)
    {
        _localizer = localizer;
    }

    public string Enhancer_Name(EnhancerId enhancerId)
    {
        return _localizer[$"Enhancer_{enhancerId}_Name"];
    }

    public string? Enhancer_Description(EnhancerId enhancerId)
    {
        var d = _localizer[$"Enhancer_{enhancerId}_Description"];
        if (d.ResourceNotFound)
        {
            return null;
        }

        return d;
    }

    public string Enhancer_TargetName(EnhancerId enhancerId, Enum target)
    {
        return _localizer[$"Enhancer_{enhancerId}_Target_{target}_Name"];
    }

    public string? Enhancer_TargetDescription(EnhancerId enhancerId, Enum target)
    {
        var d = _localizer[$"Enhancer_{enhancerId}_Target_{target}_Description"];
        if (d.ResourceNotFound)
        {
            return null;
        }

        return d;
    }
}