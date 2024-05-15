using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.StandardValue
{
    public static class StandardValueExtensions
    {
        public static (HashSet<string> StringValues, Func<Dictionary<string, string>, object> ReplaceWithAlias)?
            BuildContextForReplaceValueWithAlias(this object value, StandardValueType valueType)
        {
            switch (valueType)
            {
                case StandardValueType.String:
                {
                    if (value is string tv)
                    {
                        return ([tv], aMap => aMap[tv]);
                    }

                    break;
                }
                case StandardValueType.ListString:
                {
                    if (value is List<string> tv)
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
                    if (value is List<List<string>> tv)
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

        public static IServiceCollection AddStandardValue<TStandardValueService>(this IServiceCollection services) where TStandardValueService: class, IStandardValueService
        {
            return services.AddScoped<IStandardValueService, TStandardValueService>();
        }
    }
}