using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Enhancers.Bangumi;

public class BangumiEnhancer : AbstractEnhancer<BangumiEnhancerTarget, BangumiEnhancerContext, object?>
{
    public BangumiEnhancer(IEnumerable<IStandardValueHandler> valueConverters, ILoggerFactory loggerFactory) : base(valueConverters, loggerFactory)
    {
    }

    protected override async Task<BangumiEnhancerContext> BuildContext(global::Bakabase.Abstractions.Models.Domain.Resource resource)
    {
        throw new System.NotImplementedException();
    }

    public override EnhancerId Id { get; }
    protected override async Task<Dictionary<BangumiEnhancerTarget, object?>?> ConvertContextByTargets(BangumiEnhancerContext context)
    {
        throw new System.NotImplementedException();
    }
}