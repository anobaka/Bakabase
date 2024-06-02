using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Components.Alias;

public class AliasService(IServiceProvider serviceProvider)
    : FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.Alias, int>(serviceProvider), IAliasService
{
    public async Task AddRange(HashSet<string> texts)
    {

    }

    public async Task SaveByResources(List<Models.Domain.Resource> resources)
    {
        var texts = resources.SelectMany(r => r.ExtractAliasTexts()).ToHashSet();
        var dbAliases = await GetAll(x => texts.Contains(x.Text));
        var dbTexts = dbAliases.Select(a => a.Text).ToHashSet();
        var newAliases = texts.Except(dbTexts).Select(t => new Abstractions.Models.Db.Alias { Text = t }).ToList();
        await base.AddRange(newAliases);
    }
}