using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.StandardValue.Abstractions.Components;

public interface IStandardValueLocalizer
{
    string HandlerNotFound(StandardValueType type);
    string ConversionRuleName(StandardValueConversionRule rule);
    string? ConversionRuleDescription(StandardValueConversionRule rule);
}