using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.StandardValue;

public class StandardValueService
{
    private readonly AliasService _aliasService;

    public StandardValueService(AliasService aliasService)
    {
        _aliasService = aliasService;
    }

    public async Task IntegrateWithAlias(Dictionary<object, StandardValueType> values)
    {
        var texts = new HashSet<string>();
        foreach (var (v, t) in values)
        {
            var tmpTexts = v.ExtractTextsForAlias(t);
            if (tmpTexts != null)
            {
                foreach (var tt in tmpTexts)
                {
                    texts.Add(tt);
                }
            }
        }

        var aliasMap = _aliasService.GetPreferredNames(texts);

    }
}