using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Alias.Extensions;

public static class AliasExtensions
{
    public static Abstractions.Models.Domain.Alias ToDomainModel(this Abstractions.Models.Db.Alias dbModel)
    {
        return new Abstractions.Models.Domain.Alias
        {
            Preferred = dbModel.Preferred,
            Text = dbModel.Text
        };
    }

    public static Abstractions.Models.Db.Alias ToDbModel(this Abstractions.Models.Domain.Alias domainModel)
    {
        return new Abstractions.Models.Db.Alias
        {
            Preferred = domainModel.Preferred,
            Text = domainModel.Text
        };
    }

    public static HashSet<string> ExtractAliasTexts(this Bakabase.Abstractions.Models.Domain.Resource resource)
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

    public static (HashSet<string> StringValues, Func<Dictionary<string, string>, object> ReplaceWithAlias)?
        BuildContextForReplacingValueWithAlias(this object bizValue, StandardValueType bizValueType)
    {
        switch (bizValueType)
        {
            case StandardValueType.String:
                {
                    if (bizValue is string tv)
                    {
                        return ([tv], aMap => aMap[tv]);
                    }

                    break;
                }
            case StandardValueType.ListString:
                {
                    if (bizValue is List<string> tv)
                    {
                        var set = tv.ToHashSet();
                        return (set, aMap => tv.Select(s => aMap[s]).ToList());
                    }

                    break;
                }
            case StandardValueType.Boolean:
                break;
            case StandardValueType.Link:
                break;
            case StandardValueType.DateTime:
                break;
            case StandardValueType.Time:
                break;
            case StandardValueType.ListListString:
                {
                    if (bizValue is List<List<string>> tv)
                    {
                        var set = tv.SelectMany(v => v).ToHashSet();
                        return (set, aMap => tv.Select(vl => vl.Select(v => aMap[v]).ToList()).ToList());
                    }

                    break;
                }
            case StandardValueType.Decimal:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return null;
    }
}