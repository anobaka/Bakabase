using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Enhancer;

public record EnhancerTargetDescriptor(int Id, string Name, StandardValueType ValueType, string? Description);