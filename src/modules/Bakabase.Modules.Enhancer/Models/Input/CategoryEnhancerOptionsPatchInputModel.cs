using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Models.Input;

public record CategoryEnhancerOptionsPatchInputModel(EnhancerFullOptions? Options, bool? Active);