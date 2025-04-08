using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Kodi;

// public class KodiEnhancer(ILoggerFactory loggerFactory, FileManager fileManager)
//     : AbstractEnhancer<ExHentaiEnhancerTarget, KodiEnhancerContext, object?>(loggerFactory, fileManager)
// {
//     protected override async Task<KodiEnhancerContext?> BuildContext(Resource resource, EnhancerFullOptions options,
//         CancellationToken ct)
//     {
//         throw new NotImplementedException();
//     }
//
//     protected override EnhancerId TypedId { get; }
//
//     protected override async Task<List<EnhancementTargetValue<ExHentaiEnhancerTarget>>> ConvertContextByTargets(
//         KodiEnhancerContext context, CancellationToken ct)
//     {
//         throw new NotImplementedException();
//     }
// }