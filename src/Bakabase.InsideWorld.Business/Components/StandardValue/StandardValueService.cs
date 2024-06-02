using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.StandardValue;

public class StandardValueService(AliasService aliasService) : IStandardValueService
{
    public async Task<Dictionary<object, object>> IntegrateWithAlias(Dictionary<object, StandardValueType> values)
    {
        var texts = new HashSet<string>();
        var valueReplacer = new Dictionary<object, Func<Dictionary<string, string>, object>>();
        foreach (var (v, t) in values)
        {
            var ctx = v.BuildContextForReplacingValueWithAlias(t);
            if (ctx.HasValue)
            {
                foreach (var tt in ctx.Value.StringValues)
                {
                    texts.Add(tt);
                }

                valueReplacer[v] = ctx.Value.ReplaceWithAlias;
            }
        }

        var aliasMap = await aliasService.GetPreferredNames(texts);

        var replacedValues =
            values.Keys.ToDictionary(d => d, d => valueReplacer.GetValueOrDefault(d)?.Invoke(aliasMap) ?? d);
        return replacedValues;
    }
}