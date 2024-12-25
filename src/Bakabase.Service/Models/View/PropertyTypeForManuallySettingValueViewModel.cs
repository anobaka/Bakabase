using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.View;

public record PropertyTypeForManuallySettingValueViewModel(PropertyType Type, bool IsAvailable, string? UnavailableReason);