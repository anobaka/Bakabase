using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.StandardValue
{
    public static class StandardValueExtensions
    {
        public static string? Serialize(this object? value)
        {
            return value == null ? null : JsonConvert.SerializeObject(value);
        }

        public static HashSet<string>? ExtractTextsForAlias(this object value, StandardValueType valueType)
        {
            switch (valueType)
            {
                case StandardValueType.String:
                {
                    if (value is string tv)
                    {
                        return [tv];
                    }

                    break;
                }
                case StandardValueType.ListString:
                {
                    if (value is List<string> tv)
                    {
                        return tv.ToHashSet();
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
                        return tv.SelectMany(v => v).ToHashSet();
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return null;
        }
    }
}