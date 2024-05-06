using Bakabase.Modules.Enhancer.Models.Domain;

namespace Bakabase.Modules.Enhancer.Models.Input;

public record CategoryEnhancerOptionsPatchInputModel(EnhancerFullOptions? Options, bool? Active);