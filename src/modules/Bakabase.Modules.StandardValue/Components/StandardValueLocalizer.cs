using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.Localization;

namespace Bakabase.Modules.StandardValue.Components;

internal class StandardValueLocalizer(IStringLocalizer<StandardValueResource> localizer) : IStandardValueLocalizer
{
    public string HandlerNotFound(StandardValueType type)
    {
        return localizer[nameof(HandlerNotFound), type];
    }

    public string ConversionRuleName(StandardValueConversionRule rule)
    {
        return localizer[$"ConversionRule_{rule}_Name"];
    }

    public string? ConversionRuleDescription(StandardValueConversionRule rule)
    {
        var x = localizer[$"ConversionRule_{rule}_Description"];
        if (x.ResourceNotFound || string.IsNullOrEmpty(x))
        {
            return null;
        }

        return x;
    }
}