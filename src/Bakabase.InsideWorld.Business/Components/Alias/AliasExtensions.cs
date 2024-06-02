
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.StandardValue;

namespace Bakabase.InsideWorld.Business.Components.Alias;

public static class AliasExtensions
{
    public static HashSet<string> ExtractAliasTexts(this Models.Domain.Resource resource)
    {
        var set = new HashSet<string>();
        if (resource.Properties != null)
        {
            foreach (var pMap in resource.Properties.Values)
            {
                foreach (var p in pMap.Values)
                {
                    if (p.Values != null)
                    {
                        foreach (var scopedValue in p.Values)
                        {
                            var ctx = scopedValue.BizValue?.BuildContextForReplacingValueWithAlias(p.BizValueType);
                            if (ctx.HasValue)
                            {
                                set.UnionWith(ctx.Value.StringValues);
                            }
                        }
                    }
                }
            }
        }

        return set;
    }
}