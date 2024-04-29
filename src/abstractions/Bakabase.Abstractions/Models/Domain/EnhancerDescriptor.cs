using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record EnhancerDescriptor(int Id, string Name, string? Description, EnhancerTargetDescriptor[] Targets);