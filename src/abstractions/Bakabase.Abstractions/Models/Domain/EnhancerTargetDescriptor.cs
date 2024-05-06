using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record EnhancerTargetDescriptor(Enum Id, string Name, StandardValueType ValueType, string? Description, int[]? OptionsItems);