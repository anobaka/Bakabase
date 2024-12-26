using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.View;

public record PropertyTypeForManuallySettingValueViewModel(
    PropertyType Type,
    StandardValueType DbValueType,
    StandardValueType BizValueType,
    bool IsReferenceValueType,
    PropertyViewModel[]? Properties,
    string? UnavailableReason)
{
    public bool IsAvailable => !IsReferenceValueType || Properties?.Any() == true;
}