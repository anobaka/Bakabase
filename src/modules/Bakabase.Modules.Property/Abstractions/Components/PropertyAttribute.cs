using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Abstractions.Components;

public record PropertyAttribute(StandardValueType DbValueType, StandardValueType BizValueType, bool IsReferenceValueType);