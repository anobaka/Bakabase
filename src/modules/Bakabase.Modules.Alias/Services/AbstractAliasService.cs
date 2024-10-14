using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.Alias.Extensions;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Bakabase.Modules.Alias.Abstractions.Models.Domain;
using Bakabase.Modules.Alias.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using System.Linq;
using Bakabase.Modules.Alias.Models.Domain;
using Bakabase.Modules.Alias.Models.Domain.Constants;

namespace Bakabase.Modules.Alias.Services;

public abstract class AbstractAliasService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, Abstractions.Models.Db.Alias, int> orm) : IAliasService
    where TDbContext : DbContext
{
    public async Task<DataChangeViewModel> SaveByResources(List<Bakabase.Abstractions.Models.Domain.Resource> resources)
    {
        var texts = resources.SelectMany(r => r.ExtractAliasTexts()).ToHashSet();
        var dbAliases = await orm.GetAll(x => texts.Contains(x.Text));
        var dbTexts = dbAliases.Select(a => a.Text).ToHashSet();
        var newAliases = texts.Except(dbTexts).Select(t => new Abstractions.Models.Db.Alias {Text = t})
            .ToList();
        await orm.AddRange(newAliases);

        return new("Alias", newAliases.Count, 0, 0);
    }

    public async Task<SingletonResponse<Abstractions.Models.Domain.Alias>> Add(AliasAddInputModel model)
    {
        var dbAlias = await orm.GetFirst(x => x.Text == model.Text);
        if (dbAlias != null)
        {
            return new SingletonResponse<Abstractions.Models.Domain.Alias>(dbAlias.ToDomainModel());
        }

        dbAlias = new Abstractions.Models.Db.Alias {Text = model.Text, Preferred = model.Preferred};
        dbAlias = (await orm.Add(dbAlias)).Data;
        return new SingletonResponse<Abstractions.Models.Domain.Alias>(dbAlias.ToDomainModel());
    }

    public async Task<BaseResponse> Patch(string text, AliasPatchInputModel model)
    {
        var alias = await orm.GetFirst(x => x.Text == text);
        if (!string.IsNullOrEmpty(model.Text))
        {
            alias.Text = model.Text;
            await orm.Update(alias);
        }

        if (model.IsPreferred)
        {
            if (!string.IsNullOrEmpty(alias.Preferred))
            {
                var otherAliases = await orm.GetAll(x =>
                    (x.Preferred == alias.Preferred || x.Text == alias.Preferred) && x.Text != text);
                foreach (var otherAlias in otherAliases)
                {
                    otherAlias.Preferred = alias.Text;
                }

                alias.Preferred = null;
                await orm.UpdateRange([.. otherAliases, alias]);
            }
        }

        return BaseResponseBuilder.Ok;
    }

    public async Task<List<Abstractions.Models.Domain.Alias>> GetAll(
        Expression<Func<Abstractions.Models.Db.Alias, bool>>? selector = null)
    {
        return (await orm.GetAll(selector)).Select(a => a.ToDomainModel()).ToList();
    }

    public async Task<int> Count(Func<Abstractions.Models.Db.Alias, bool>? selector = null)
    {
        return await orm.Count(selector);
    }

    public async Task<bool> Any(Func<Abstractions.Models.Db.Alias, bool>? selector = null)
    {
        return await orm.Any(selector);
    }

    public async Task<SearchResponse<Abstractions.Models.Domain.Alias>> SearchGroups(AliasSearchInputModel model)
    {
        Expression<Func<Abstractions.Models.Db.Alias, bool>>? exp = null;
        if (!string.IsNullOrEmpty(model.Text))
        {
            exp = exp.And(b => b.Text == model.Text);
        }

        if (!string.IsNullOrEmpty(model.FuzzyText))
        {
            exp = exp.And(a => a.Text.Contains(model.FuzzyText));
        }

        if (model.Texts?.Any() == true)
        {
            exp = exp.And(a => model.Texts.Contains(a.Text));
        }

        var func = exp?.Compile();

        var allAliases = await orm.GetAll(null, false);
        var allGroups = allAliases.GroupBy(a => a.Preferred ?? a.Text).Select(a => a.ToList()).ToList();
        var allHitGroups = func == null ? allGroups : allGroups.Where(x => x.Any(y => func(y))).ToList();
        var pageableGroups = allHitGroups.Skip((model.PageIndex - 1) * model.PageSize).Take(model.PageSize).ToList();

        var doModels = pageableGroups.Select(g =>
        {
            var preferred = g.First(x => string.IsNullOrEmpty(x.Preferred));
            var preferredDoModel = preferred.ToDomainModel();
            preferredDoModel.Candidates = g.Where(x => x != preferred).Select(x => x.Text).ToHashSet();
            return preferredDoModel;
        }).ToList();

        return model.BuildResponse(doModels, allHitGroups.Count);
    }

    public async Task<BaseResponse> Delete(string text) => await orm.RemoveAll(x => x.Text == text);

    public async Task<BaseResponse> DeleteGroups(string[] preferredTexts) =>
        await orm.RemoveAll(x => preferredTexts.Contains(x.Text) || preferredTexts.Contains(x.Preferred));

    public async Task<BaseResponse> MergeGroups(string[] preferredTexts)
    {
        var preferred = preferredTexts[0];
        var aliases = await orm.GetAll(x => preferredTexts.Contains(x.Text) || preferredTexts.Contains(x.Preferred));
        foreach (var a in aliases)
        {
            a.Preferred = a.Text == preferred ? null : preferred;
        }

        await orm.UpdateRange(aliases);
        return BaseResponseBuilder.Ok;
    }

    private async Task<Dictionary<string, string>> GetPreferredNames(HashSet<string> texts)
    {
        var dbAliases = await orm.GetAll(x => texts.Contains(x.Text));
        var aliasMap = dbAliases.ToDictionary(a => a.Text, a => a.Preferred ?? a.Text);
        return texts.ToDictionary(d => d, d => aliasMap.GetValueOrDefault(d) ?? d);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public async Task<List<object>> GetAliasAppliedValues(
        List<(object BizValue, StandardValueType BizValueType)> values)
    {
        var texts = new HashSet<string>();
        var valueReplacer = new List<Func<Dictionary<string, string>, object>?>();
        foreach (var (v, t) in values)
        {
            var ctx = v.BuildContextForReplacingValueWithAlias(t);
            if (ctx.HasValue)
            {
                foreach (var tt in ctx.Value.StringValues)
                {
                    texts.Add(tt);
                }
            }

            valueReplacer.Add(ctx?.ReplaceWithAlias);
        }

        var aliasMap = await GetPreferredNames(texts);

        var replacedValues = values.Select((d, i) => valueReplacer[i]?.Invoke(aliasMap) ?? d.BizValue).ToList();
        return replacedValues;
    }

    public async Task MergeGroups(string[][] aliasGroups)
    {
        var aliases = await orm.GetAll();
        var groupedAliases = aliases.GroupBy(t => t.Preferred ?? t.Text).ToDictionary(a => a.Key, a => a.ToArray());
        var textGroupMap = groupedAliases.SelectMany(x => x.Value.Select(a => (Name: a.Text, Group: x.Value)))
            .ToDictionary(d => d.Name, d => d.Group);
        var texts = textGroupMap.Keys.ToHashSet();

        var errors = new List<string>();
        var newAliases = new List<Abstractions.Models.Db.Alias>();
        var changedAliases = new List<Abstractions.Models.Db.Alias>();

        var optimizedAliasGroups = aliasGroups.Select(x => new[] {x[0]}.Concat(x.Skip(1).Distinct()).ToArray()).ToArray();

        foreach (var incomingGroup in optimizedAliasGroups)
        {
            var dbTexts = incomingGroup.Where(texts.Contains).ToArray();
            var dbGroups = dbTexts.Select(t => textGroupMap[t]).Distinct().ToList();
            if (dbGroups.Count > 1)
            {
                var msg =
                    $"[{string.Join(",", incomingGroup)}] exist in more than one alias group on words: [{string.Join(",", dbTexts)}]";
                errors.Add(msg);
                continue;
            }

            var newPreferred = incomingGroup[0];
            if (dbGroups.Any())
            {
                var dbGroup = dbGroups[0];
                var @new = incomingGroup.Except(dbGroup.Select(g => g.Text)).Select(a =>
                    new Abstractions.Models.Db.Alias
                    {
                        Preferred = a == newPreferred ? null : newPreferred,
                        Text = a
                    }).ToArray();
                newAliases.AddRange(@new);

                foreach (var dbAlias in dbGroup)
                {
                    if (dbAlias.Text == newPreferred)
                    {
                        if (dbAlias.Preferred != null)
                        {
                            dbAlias.Preferred = null;
                            changedAliases.Add(dbAlias);
                        }
                    }
                    else
                    {
                        if (dbAlias.Preferred != newPreferred)
                        {
                            dbAlias.Preferred = newPreferred;
                            changedAliases.Add(dbAlias);
                        }
                    }
                }
            }
            else
            {
                var @new = incomingGroup.Select(t => new Abstractions.Models.Db.Alias
                {
                    Text = t,
                    Preferred = t == newPreferred ? null : newPreferred,
                });
                newAliases.AddRange(@new);
            }
        }

        await orm.AddRange(newAliases);
        await orm.UpdateRange(changedAliases);

        if (errors.Any())
        {
            throw new AliasException(AliasExceptionType.ConflictAliasGroup, string.Join(Environment.NewLine, errors));
        }
    }
}