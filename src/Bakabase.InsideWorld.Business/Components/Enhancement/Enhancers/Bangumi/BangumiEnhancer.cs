using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Enhancers.Bangumi;

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