using Bakabase.Abstractions.Components.Enhancer;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record EnhancerDescriptor(EnhancerId Id, string Name, string? Description, EnhancerTargetDescriptor[] Targets);