using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Models.View;

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